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
using Microsoft.Extensions.Logging.Abstractions;

namespace Bookmarkarr.Tests.Features.Application.Audiobooks.Quality
{
    [Trait("Name", "QualityProfileSizeLimitTests")]
    [Trait("Category", "QualityProfile")]
    public class QualityProfileSizeLimitTests
    {
        private static QualityProfileService CreateService() =>
            new(Mock.Of<IQualityProfileRepository>(), NullLogger<QualityProfileService>.Instance);

        private static QualityProfile ProfileWithMaximumSize(int megabytes) => new()
        {
            MinimumSize = 0,
            MaximumSize = megabytes,
            PreferredFormats = ["m4b"],
            PreferredWords = [],
            MustNotContain = [],
            MustContain = [],
            PreferredLanguages = ["English"],
            MinimumSeeders = 0,
            MaximumAge = 0
        };

        private static SearchResult ResultOfSize(long bytes) => new()
        {
            Title = "Author - Great Book (unabridged)",
            Size = bytes,
            Format = "m4b",
            Quality = "320",
            Language = "English",
            DownloadType = "torrent",
            Seeders = 5,
            PublishedDate = DateTime.UtcNow.ToString("o")
        };

        [Theory]
        [Trait("Scenario", "A limit above 2047 MB must not overflow into rejecting everything")]
        [InlineData(2048)]
        [InlineData(5 * 1024)]
        [InlineData(int.MaxValue / (1024 * 1024))]
        public async Task ScoreSearchResult_WithLargeMaximumSize_AcceptsAnOrdinaryRelease(int maximumSizeMegabytes)
        {
            // Regression: the megabyte-to-byte conversion ran in int arithmetic, so any limit above
            // 2047 MB wrapped to a negative number. Every release then compared as "larger than the
            // maximum" and the profile silently rejected everything it scored — which is exactly
            // what a 5 GB default would have triggered on every install.
            var service = CreateService();

            var score = await service.ScoreSearchResult(
                ResultOfSize(750L * 1024 * 1024),
                ProfileWithMaximumSize(maximumSizeMegabytes));

            Assert.DoesNotContain(score.RejectionReasons, reason => reason.Contains("too large", StringComparison.OrdinalIgnoreCase));
            Assert.NotEqual(-1, score.TotalScore);
        }

        [Fact]
        [Trait("Scenario", "The cap still rejects something genuinely over it")]
        public async Task ScoreSearchResult_WhenResultExceedsTheCap_IsRejected()
        {
            var service = CreateService();

            var score = await service.ScoreSearchResult(
                ResultOfSize(6L * 1024 * 1024 * 1024),
                ProfileWithMaximumSize(5 * 1024));

            Assert.Contains(score.RejectionReasons, reason => reason.Contains("too large", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(-1, score.TotalScore);
        }

        [Fact]
        [Trait("Scenario", "A new profile starts with the 5 GB guard rather than unlimited")]
        public void NewQualityProfile_DefaultsToAFiveGigabyteCap()
        {
            Assert.Equal(5 * 1024, new QualityProfile().MaximumSize);
        }
    }
}
