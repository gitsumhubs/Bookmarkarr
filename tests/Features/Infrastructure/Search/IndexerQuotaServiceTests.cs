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
        // The general rules are tested on an ordinary indexer, where a search costs one request.
        // AudioBook Bay has its own rules and its own tests at the bottom.
        private static (IndexerQuotaService Service, BookmarkarrDbContext Db) Build(
            int? requestsPerHour, string name = "Some Indexer", string url = "http://indexer.invalid/api")
        {
            var options = new DbContextOptionsBuilder<BookmarkarrDbContext>()
                .UseInMemoryDatabase("quota_" + Guid.NewGuid().ToString("N"))
                .Options;
            var db = new BookmarkarrDbContext(options);

            db.Indexers.Add(new Indexer
            {
                Id = 1,
                Name = name,
                Type = "Torrent",
                Implementation = "Torznab",
                Url = url,
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

        [Fact]
        public async Task AnAudiobookBayIndexerWithNoBudgetSet_StillGetsOne()
        {
            var (service, _) = Build(requestsPerHour: null, name: "AudioBook Bay (Prowlarr)");

            var state = await service.GetStateAsync(1);

            Assert.True(state.IsLimited);
            Assert.Equal(AudiobookBayPolicy.DefaultRequestsPerHour, state.RequestsPerHour);
        }

        [Fact]
        public async Task AnAudiobookBaySearch_IsChargedOneRequestPerPage()
        {
            // 60 an hour with 12 held back for a person leaves automation 48: sixteen searches of
            // three pages, not forty-eight.
            var (service, db) = Build(requestsPerHour: null, name: "AudioBook Bay (Prowlarr)");

            for (var i = 0; i < 16; i++)
            {
                Assert.True(await service.TryConsumeAsync(1, IndexerQuotaPurpose.AutomaticSearch));
            }

            Assert.Equal(48, db.IndexerQuotaUsages.Count());
            Assert.False(await service.TryConsumeAsync(1, IndexerQuotaPurpose.AutomaticSearch));
            Assert.NotNull(await service.TimeUntilAvailableAsync(1, IndexerQuotaPurpose.AutomaticSearch));

            // A grab is one request to the site, whatever the search cost.
            Assert.True(await service.TryConsumeAsync(1, IndexerQuotaPurpose.Grab));
            Assert.Equal(49, db.IndexerQuotaUsages.Count());
        }

        [Fact]
        public async Task TheRecordedPatchPageCount_IsWhatASearchCosts()
        {
            var (service, db) = Build(requestsPerHour: 60, name: "AudioBook Bay");
            db.ApplicationSettings.Add(new ApplicationSettings { AudiobookBayPatchIndexerId = 1, AudiobookBayPatchPages = 5 });
            db.SaveChanges();

            Assert.True(await service.TryConsumeAsync(1, IndexerQuotaPurpose.InteractiveSearch));

            Assert.Equal(5, db.IndexerQuotaUsages.Count());
        }

        [Fact]
        public async Task ARefusal_ShutsTheIndexer_ForLongerEachTime_UntilItAnswers()
        {
            var (service, db) = Build(requestsPerHour: null);

            var first = await service.RecordFailureAsync(1, rateLimited: true, retryAfter: null, "429 Too Many Requests");
            Assert.InRange(first - DateTime.UtcNow, TimeSpan.FromMinutes(59), TimeSpan.FromMinutes(61));
            Assert.False(await service.TryConsumeAsync(1, IndexerQuotaPurpose.InteractiveSearch));
            Assert.NotNull(await service.TimeUntilAvailableAsync(1, IndexerQuotaPurpose.AutomaticSearch));
            Assert.Equal("429 Too Many Requests", (await service.GetCooldownAsync(1))!.Reason);

            var second = await service.RecordFailureAsync(1, rateLimited: true, retryAfter: null, "429 Too Many Requests");
            Assert.InRange(second - DateTime.UtcNow, TimeSpan.FromHours(5.9), TimeSpan.FromHours(6.1));

            // Stored on the indexer, so a restart does not hand the site a fresh start.
            Assert.Equal(2, db.Indexers.Single().ConsecutiveFailures);

            await service.RecordSuccessAsync(1);
            Assert.Null(await service.GetCooldownAsync(1));
            Assert.True(await service.TryConsumeAsync(1, IndexerQuotaPurpose.AutomaticSearch));
        }

        [Fact]
        public async Task AFailureThatIsNotARefusal_IsAShortWait_AndRetryAfterIsHonoured()
        {
            var (service, _) = Build(requestsPerHour: null);

            var until = await service.RecordFailureAsync(1, rateLimited: false, retryAfter: null, "did not answer");
            Assert.InRange(until - DateTime.UtcNow, TimeSpan.FromMinutes(9), TimeSpan.FromMinutes(11));

            var asked = await service.RecordFailureAsync(1, rateLimited: false, retryAfter: TimeSpan.FromHours(3), "503");
            Assert.InRange(asked - DateTime.UtcNow, TimeSpan.FromHours(2.9), TimeSpan.FromHours(3.1));
        }
    }
}
