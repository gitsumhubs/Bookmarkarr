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

using Bookmarkarr.Domain.Search;

namespace Bookmarkarr.Tests.Features.Domain.Search
{
    public class UnattendedSearchRulesTests
    {
        private const long Megabyte = 1024 * 1024;

        [Theory]
        [InlineData(9, false, 0, false)]          // a 9 MB "audiobook"
        [InlineData(70, false, 0, true)]          // a short book
        [InlineData(5128, false, 0, true)]        // a long book in a heavy format
        [InlineData(7684, false, 0, false)]       // nine books in one
        [InlineData(807316, false, 0, false)]     // the 846 GB collection
        [InlineData(7684, false, 10240, true)]    // the profile says 10 GB, so 7.5 GB is allowed
        [InlineData(3000, false, 2048, false)]    // the profile says 2 GB
        [InlineData(2, true, 0, true)]            // an ebook has no floor
        [InlineData(900, true, 0, false)]         // an ebook pack
        [InlineData(0, false, 0, true)]           // size unknown: not judged
        public void SizeGuard(long megabytes, bool ebook, int profileMaximum, bool allowed)
        {
            Assert.Equal(allowed, ReleaseSizeGuard.Check(megabytes * Megabyte, ebook, profileMaximum) == null);
        }

        [Theory]
        [InlineData("German", "The Name of the Wind", null, false)]                           // the indexer says German
        [InlineData(null, "The Name of the Wind [German]", null, false)]                      // ABB's line, carried in brackets
        [InlineData(null, "Der Name des Windes (DE)", null, false)]
        [InlineData("English", "The Name of the Wind", null, true)]
        [InlineData(null, "The Name of the Wind - Patrick Rothfuss [M4B]", null, true)]       // says nothing: not judged
        [InlineData(null, "Discount Dan - Cul-de-Sac Carnage [M4B]", null, true)]             // "de" in a word is not German
        [InlineData("German", "Der Name des Windes", "german", true)]                          // the book itself is German
        [InlineData("Italian", "Il nome del vento", "english", false)]
        public void LanguageGuard_RefusesOnlyAStatedOtherLanguage(string? releaseLanguage, string title, string? bookLanguage, bool allowed)
        {
            Assert.Equal(allowed, ReleaseLanguageGuard.Check(releaseLanguage, title, bookLanguage, ["English"]) == null);
        }

        [Fact]
        public void LanguageGuard_WithNoWantedLanguage_JudgesNothing()
        {
            Assert.Null(ReleaseLanguageGuard.Check("German", "Title", null, []));
        }

        [Fact]
        public void LanguageGuard_SaysWhichLanguagesWereWanted()
        {
            Assert.Equal("in German, not English", ReleaseLanguageGuard.Check("German", "Title", null, ["English"]));
        }

        [Theory]
        [InlineData(1, 6)]
        [InlineData(2, 12)]
        [InlineData(3, 24)]
        [InlineData(5, 96)]
        [InlineData(6, 168)]
        [InlineData(40, 168)]
        public void AMissedBook_WaitsTwiceAsLongEachTime_UpToAWeek(int misses, int hours)
        {
            Assert.Equal(TimeSpan.FromHours(hours), SearchBackoff.AfterMisses(misses));
        }

        [Theory]
        [InlineData("AudioBook Bay (Prowlarr)", "http://prowlarr.invalid/2/api", true)]
        [InlineData("ABB", "https://audiobookbay.invalid/", true)]
        [InlineData("Geek", "https://indexer.invalid", false)]
        public void AudiobookBay_IsRecognisedByNameOrAddress(string name, string url, bool expected)
        {
            Assert.Equal(expected, AudiobookBayPolicy.IsAudiobookBay(new Indexer { Name = name, Url = url }));
        }

        [Fact]
        public void ABudgetSomebodySet_IsLeftAlone_IncludingZero()
        {
            Assert.Equal(20, AudiobookBayPolicy.EffectiveRequestsPerHour(new Indexer { Name = "AudioBook Bay", RequestsPerHour = 20 }));
            Assert.Equal(0, AudiobookBayPolicy.EffectiveRequestsPerHour(new Indexer { Name = "AudioBook Bay", RequestsPerHour = 0 }));
            Assert.Null(AudiobookBayPolicy.EffectiveRequestsPerHour(new Indexer { Name = "Geek" }));
        }
    }
}
