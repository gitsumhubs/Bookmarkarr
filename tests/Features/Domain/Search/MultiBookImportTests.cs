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

using Bookmarkarr.Domain.Audiobooks.Rules;
using Bookmarkarr.Domain.Search;

namespace Bookmarkarr.Tests.Features.Domain.Search
{
    public class MultiBookImportTests
    {
        private static readonly WantedBook Book = new() { Title = "He Who Fights with Monsters (He Who Fights with Monsters, #1)", Authors = ["Shirtaloon"] };

        private static string[] Files(params string[] relative) => relative.Select(path => "/downloads/release/" + path).ToArray();

        [Fact]
        public void APackName_IsHeld()
        {
            var reason = MultiBookDownloadDetector.Detect(Book, "He Who Fights With Monsters, Books 1-12 - Shirtaloon [Mixed]", Files("01.mp3", "02.mp3"));
            Assert.NotNull(reason);
            Assert.Contains("books 1-12", reason);
        }

        [Fact]
        public void AudioInSeveralBookFolders_IsHeld()
        {
            var reason = MultiBookDownloadDetector.Detect(Book, "He Who Fights With Monsters - Shirtaloon",
                Files("Book 1/01.mp3", "Book 2/01.mp3", "Book 3/01.mp3"));
            Assert.NotNull(reason);
            Assert.Contains("3 separate folders", reason);
        }

        [Fact]
        public void TwoFoldersThatNumberThemselvesAsBooks_AreHeld()
        {
            Assert.NotNull(MultiBookDownloadDetector.Detect(Book, "Some Release", Files("Book 1 - First/01.mp3", "Book 2 - Second/01.mp3")));
        }

        [Theory]
        [InlineData("CD 1/01.mp3", "CD 2/01.mp3", "CD 3/01.mp3")]
        [InlineData("Disc 01/a.mp3", "Disc 02/b.mp3", "Extras/c.mp3")]
        [InlineData("Part 1/a.m4b", "Part 2/b.m4b", "Part 3/c.m4b")]
        [InlineData("01.mp3", "02.mp3", "03.mp3")]
        public void OneBookInDiscsPartsOrLooseFiles_IsNotHeld(string a, string b, string c)
        {
            Assert.Null(MultiBookDownloadDetector.Detect(Book, "He Who Fights With Monsters - Shirtaloon [M4B]", Files(a, b, c)));
        }

        [Fact]
        public void APackWordTheBookUsesItself_IsNotHeld()
        {
            var collection = new WantedBook { Title = "The Complete Sherlock Holmes Collection", Authors = ["Arthur Conan Doyle"] };
            Assert.Null(MultiBookDownloadDetector.Detect(collection, "The Complete Sherlock Holmes Collection - Arthur Conan Doyle", Files("01.mp3", "02.mp3")));
        }

        private static List<PlannedImportFile> Plan(params string[] relative) =>
            MultiFileImportPlanner.BuildPlans(relative.Select(path => ("/src/" + path, (string?)path)));

        [Fact]
        public void LooseTracks_AreOneDisc_WithTheDefaultPattern()
        {
            var plans = Plan("001.mp3", "002.mp3", "210.mp3");
            var discs = MultiFileImportPlanner.BuildDiskNumbers(plans, "{Title}-{DiskNumber:00}-{ChapterNumber:00}");
            Assert.All(discs.Values, disc => Assert.Equal(1, disc));
        }

        [Fact]
        public void DiscFolders_AreKept_AndCarriedOverTheirTracks()
        {
            var plans = Plan("CD 1/01.mp3", "CD 1/02.mp3", "CD 2/01.mp3", "CD 2/02.mp3");
            var discs = MultiFileImportPlanner.BuildDiskNumbers(plans, "{Title}-{DiskNumber:00}-{ChapterNumber:00}");
            Assert.Equal([1, 1, 2, 2], plans.OrderBy(p => p.SequenceNumber).Select(p => discs[p.FullPath]));
        }

        [Fact]
        public void APatternWithoutAChapterToken_KeepsOneDiscNumberPerFile()
        {
            // There the disc number is the only thing telling the files apart.
            var plans = Plan("01.mp3", "02.mp3", "03.mp3");
            var discs = MultiFileImportPlanner.BuildDiskNumbers(plans, "{Title}-{DiskNumber:00}");
            Assert.Equal(3, discs.Values.Distinct().Count());
        }
    }
}
