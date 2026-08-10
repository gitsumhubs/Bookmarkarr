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
using Bookmarkarr.Domain.Books;
using Bookmarkarr.Tests.Common;

namespace Bookmarkarr.Tests.Features.Infrastructure.Persistence
{
    /// <summary>
    /// A monitored edition is searched every six hours whether or not anyone wants anything from
    /// it, so a finished library keeps querying indexers forever. These cover which editions stop
    /// asking and, more importantly, which keep their monitor.
    /// </summary>
    public class UnmonitorSatisfiedEditionsTests : BaseTests
    {
        private async Task<Audiobook> AddBookAsync(string title, params BookEdition[] editions)
        {
            var book = new Audiobook
            {
                Title = title,
                Monitored = true,
                Editions = editions.ToList()
            };

            return await _audiobookRepository.AddAsync(book);
        }

        private static BookEdition Edition(
            EditionWantedStatus status,
            bool monitored = true,
            bool withFile = true)
            => new()
            {
                MediaType = EditionMediaType.Audiobook,
                Monitored = monitored,
                Status = status,
                Files = withFile
                    ? [new EditionFile { Path = $"/audiobooks/{Guid.NewGuid():N}.m4b", Extension = ".m4b", Size = 1 }]
                    : []
            };

        [Fact]
        public async Task RetiresAnImportedEditionThatHoldsFiles_AndTheBookWithIt()
        {
            var book = await AddBookAsync("Imported", Edition(EditionWantedStatus.Imported));

            var retired = await _audiobookRepository.UnmonitorSatisfiedEditionsAsync();

            Assert.Equal(1, retired);
            var reloaded = await _audiobookRepository.GetByIdAsync(book.Id);
            Assert.All(reloaded!.Editions, e => Assert.False(e.Monitored));
            // The search selects on the book, so retiring only the edition would change nothing.
            Assert.False(reloaded.Monitored);
        }

        [Fact]
        public async Task LeavesAnImportedEditionWithNoFiles_Alone()
        {
            // Imported without a file is a failed import, not a satisfied one. It still wants
            // something, and unmonitoring it would strand it permanently.
            var book = await AddBookAsync("No files", Edition(EditionWantedStatus.Imported, withFile: false));

            var retired = await _audiobookRepository.UnmonitorSatisfiedEditionsAsync();

            Assert.Equal(0, retired);
            var reloaded = await _audiobookRepository.GetByIdAsync(book.Id);
            Assert.True(reloaded!.Monitored);
        }

        [Theory]
        [InlineData(EditionWantedStatus.Missing)]
        [InlineData(EditionWantedStatus.Queued)]
        [InlineData(EditionWantedStatus.Downloading)]
        public async Task LeavesAnythingStillWanted_Alone(EditionWantedStatus status)
        {
            var book = await AddBookAsync($"Wanted {status}", Edition(status, withFile: false));

            var retired = await _audiobookRepository.UnmonitorSatisfiedEditionsAsync();

            Assert.Equal(0, retired);
            var reloaded = await _audiobookRepository.GetByIdAsync(book.Id);
            Assert.True(reloaded!.Monitored);
        }

        [Fact]
        public async Task KeepsTheBookMonitored_WhileAnyEditionStillWantsSomething()
        {
            // The audiobook arrived, the ebook has not. Retiring the book here would stop the
            // ebook being searched — the exact silent loss this sweep must not cause.
            var book = await AddBookAsync(
                "Half done",
                Edition(EditionWantedStatus.Imported),
                new BookEdition
                {
                    MediaType = EditionMediaType.Ebook,
                    Monitored = true,
                    Status = EditionWantedStatus.Missing,
                    Files = []
                });

            var retired = await _audiobookRepository.UnmonitorSatisfiedEditionsAsync();

            Assert.Equal(1, retired);
            var reloaded = await _audiobookRepository.GetByIdAsync(book.Id);
            Assert.True(reloaded!.Monitored);
            Assert.Single(reloaded.Editions, e => e.Monitored && e.MediaType == EditionMediaType.Ebook);
        }
    }
}
