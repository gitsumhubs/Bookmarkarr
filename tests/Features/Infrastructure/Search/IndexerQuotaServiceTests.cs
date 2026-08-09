/*
 * Bookmarkarr - Audiobook Management System
 * Copyright (C) 2024-2026 Bookmarkarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */

using Bookmarkarr.Infrastructure.Persistence;
using Bookmarkarr.Infrastructure.Search.Quota;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bookmarkarr.Tests.Features.Infrastructure.Search
{
    /// <summary>
    /// The budget exists to keep Bookmarkarr under a remote site's ban threshold, so the rules that
    /// matter are: an unlimited indexer is never impeded, automation stops at the reserve while a
    /// person does not, nothing is spendable past the limit, and the countdown reports when a real
    /// slot returns.
    /// </summary>
    public class IndexerQuotaServiceTests
    {
        private static (IndexerQuotaService Service, BookmarkarrDbContext Db) Build(int? requestsPerHour)
        {
            var options = new DbContextOptionsBuilder<BookmarkarrDbContext>()
                .UseInMemoryDatabase("quota_" + Guid.NewGuid().ToString("N"))
                .Options;
            var db = new BookmarkarrDbContext(options);

            db.Indexers.Add(new Indexer
            {
                Id = 1,
                Name = "AudioBook Bay",
                Type = "Torrent",
                Implementation = "Torznab",
                Url = "http://prowlarr:9696/2/api",
                RequestsPerHour = requestsPerHour
            });
            db.SaveChanges();

            return (new IndexerQuotaService(db, NullLogger<IndexerQuotaService>.Instance), db);
        }

        [Fact]
        public async Task AnUnlimitedIndexer_NeverBlocks_AndRecordsNothing()
        {
            var (service, db) = Build(requestsPerHour: null);

            for (var i = 0; i < 50; i++)
            {
                Assert.True(await service.TryConsumeAsync(1, IndexerQuotaPurpose.AutomaticSearch));
            }

            // Writing usage for an unlimited indexer would grow forever for no purpose.
            Assert.Empty(db.IndexerQuotaUsages);
            Assert.Null(await service.TimeUntilAvailableAsync(1, IndexerQuotaPurpose.AutomaticSearch));
        }

        [Fact]
        public async Task AutomationStopsAtTheReserve_WhileAPersonCanStillSearch()
        {
            // 10/hour reserves 2 for interactive use, leaving automation 8.
            var (service, _) = Build(requestsPerHour: 10);

            for (var i = 0; i < 8; i++)
            {
                Assert.True(await service.TryConsumeAsync(1, IndexerQuotaPurpose.AutomaticSearch));
            }

            Assert.False(await service.TryConsumeAsync(1, IndexerQuotaPurpose.AutomaticSearch));

            // The reserve is exactly what keeps a person's own search working here.
            Assert.True(await service.TryConsumeAsync(1, IndexerQuotaPurpose.InteractiveSearch));
            Assert.True(await service.TryConsumeAsync(1, IndexerQuotaPurpose.Grab));
        }

        [Fact]
        public async Task NothingIsSpendablePastTheLimit_NotEvenInteractive()
        {
            var (service, _) = Build(requestsPerHour: 4);

            for (var i = 0; i < 4; i++)
            {
                Assert.True(await service.TryConsumeAsync(1, IndexerQuotaPurpose.InteractiveSearch));
            }

            // One more request is precisely what re-arms a ban, so the human is stopped too.
            Assert.False(await service.TryConsumeAsync(1, IndexerQuotaPurpose.InteractiveSearch));
            Assert.False(await service.TryConsumeAsync(1, IndexerQuotaPurpose.Grab));
            Assert.False(await service.TryConsumeAsync(1, IndexerQuotaPurpose.AutomaticSearch));
        }

        [Fact]
        public async Task TheCountdownReportsWhenTheOldestRequestAgesOut()
        {
            var (service, db) = Build(requestsPerHour: 2);

            // Spent 50 minutes ago, so capacity returns in about 10.
            db.IndexerQuotaUsages.Add(new IndexerQuotaUsage
            {
                IndexerId = 1,
                OccurredAtUtc = DateTime.UtcNow.AddMinutes(-50),
                Purpose = IndexerQuotaPurpose.InteractiveSearch
            });
            db.IndexerQuotaUsages.Add(new IndexerQuotaUsage
            {
                IndexerId = 1,
                OccurredAtUtc = DateTime.UtcNow.AddMinutes(-5),
                Purpose = IndexerQuotaPurpose.InteractiveSearch
            });
            await db.SaveChangesAsync();

            Assert.False(await service.TryConsumeAsync(1, IndexerQuotaPurpose.InteractiveSearch));

            var wait = await service.TimeUntilAvailableAsync(1, IndexerQuotaPurpose.InteractiveSearch);
            Assert.NotNull(wait);
            Assert.InRange(wait!.Value.TotalMinutes, 8, 12);
        }

        [Fact]
        public async Task RequestsOutsideTheWindowDoNotCount()
        {
            var (service, db) = Build(requestsPerHour: 2);

            db.IndexerQuotaUsages.Add(new IndexerQuotaUsage
            {
                IndexerId = 1,
                OccurredAtUtc = DateTime.UtcNow.AddMinutes(-61),
                Purpose = IndexerQuotaPurpose.AutomaticSearch
            });
            await db.SaveChangesAsync();

            var state = await service.GetStateAsync(1);
            Assert.Equal(0, state.Used);
            Assert.True(await service.TryConsumeAsync(1, IndexerQuotaPurpose.AutomaticSearch));
        }

        [Fact]
        public async Task OneIndexersSpendingDoesNotRationAnother()
        {
            var (service, db) = Build(requestsPerHour: 2);
            db.Indexers.Add(new Indexer
            {
                Id = 2,
                Name = "Geek",
                Type = "Usenet",
                Implementation = "Newznab",
                Url = "https://api.nzbgeek.info",
                RequestsPerHour = 2
            });
            await db.SaveChangesAsync();

            Assert.True(await service.TryConsumeAsync(1, IndexerQuotaPurpose.InteractiveSearch));
            Assert.True(await service.TryConsumeAsync(1, IndexerQuotaPurpose.InteractiveSearch));
            Assert.False(await service.TryConsumeAsync(1, IndexerQuotaPurpose.InteractiveSearch));

            Assert.True(await service.TryConsumeAsync(2, IndexerQuotaPurpose.InteractiveSearch));
        }
    }
}
