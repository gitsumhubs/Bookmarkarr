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

namespace Bookmarkarr.Tests.Features.Application.Search.Parsing
{
    public class ParseLanguageTests
    {
        [Theory]
        [InlineData("[ENG / M4B] Some Title", "English")]
        [InlineData("Some Title (EN)", "English")]
        [InlineData("Some Title EN", "English")]
        [InlineData("[DUT] Title", "Dutch")]
        [InlineData("Title - NL", "Dutch")]
        [InlineData("Title (DE)", "German")]
        [InlineData("[GER / MP3] Foo", "German")]
        [InlineData("Book Title FR", "French")]
        [InlineData("[FRE] Bar", "French")]
        [InlineData("No language here", null)]
        public void ParseLanguageFromText_RecognizesCodes(string input, string? expected)
        {
            var result = SearchResultAttributeParser.ParseLanguageFromText(input);
            Assert.Equal(expected, result);
        }

        // A lower-case "de", "en", "es", "fr" or "nl" in running text is a word, not a tag. The
        // first row is the release that was reported as German.
        [Theory]
        [InlineData("Discount Dan’s Backroom Bargains Book 2 - Cul-de-Sac Carnage - James Hunter [M4B]")]
        [InlineData("Tour de France: The Complete History [MP3]")]
        [InlineData("Bel-Ami - Guy de Maupassant")]
        [InlineData("Le Morte d'Arthur - Sir Thomas Malory")]
        [InlineData("Como agua para chocolate es una novela")]
        [InlineData("The Hidden Garden (de luxe edition)")]
        [InlineData("CUL-DE-SAC")]
        [InlineData("DEN OF THIEVES")]
        public void ParseLanguageFromText_IgnoresWordsThatOnlyLookLikeCodes(string input)
        {
            Assert.Null(SearchResultAttributeParser.ParseLanguageFromText(input));
        }

        [Theory]
        [InlineData("[eng / m4b] Some Title", "English")]
        [InlineData("Some Title (de)", "German")]
        [InlineData("Some Title [German]", "German")]
        [InlineData("Some Title [MP3 64kbps ENG]", "English")]
        [InlineData("Some.Book.Title.DE.MP3", "German")]
        [InlineData("Autor - Titel (Ungekürzt) [DEU]", "German")]
        public void ParseLanguageFromText_RecognizesTagsInBracketsAndCapitals(string input, string expected)
        {
            Assert.Equal(expected, SearchResultAttributeParser.ParseLanguageFromText(input));
        }

        // A language field holds nothing but the language, so its case carries no meaning.
        [Theory]
        [InlineData("en", "English")]
        [InlineData("ENG", "English")]
        [InlineData("English", "English")]
        [InlineData(" de ", "German")]
        [InlineData("deutsch", "German")]
        [InlineData("[ENG / M4B]", "English")]
        [InlineData("Klingon", null)]
        [InlineData("", null)]
        public void FromField_ReadsAnIndexerLanguageValue(string input, string? expected)
        {
            Assert.Equal(expected, ReleaseLanguageDetector.FromField(input));
        }

        [Theory]
        [InlineData("en", "English", true)]
        [InlineData("English", "english", true)]
        [InlineData("German", "de", true)]
        [InlineData("English", "German", false)]
        [InlineData("Klingon", "klingon", true)]
        [InlineData("English", null, false)]
        public void AreSameLanguage_ComparesLanguagesNotStrings(string? left, string? right, bool expected)
        {
            Assert.Equal(expected, ReleaseLanguageDetector.AreSameLanguage(left, right));
        }

        // The conversion every torrent result passes through on its way to scoring and the UI.
        [Fact]
        public void ToSearchResult_DoesNotCallAnEnglishTorrentGerman()
        {
            var converted = SearchResultConverters.ToSearchResult(new IndexerSearchResult
            {
                Title = "Discount Dan’s Backroom Bargains Book 2 - Cul-de-Sac Carnage - James Hunter [M4B]",
                DownloadType = "Torrent"
            });

            Assert.Null(converted.Language);
        }
    }
}
