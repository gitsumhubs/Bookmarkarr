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

using System.Text.RegularExpressions;

namespace Bookmarkarr.Domain.Search
{
    /// <summary>
    /// Finds the author a release name credits, when it is somebody other than the wanted author.
    /// </summary>
    /// <remarks>
    /// Indexer names are mostly "Title - Author [tags]", and usenet names often "Author - Title".
    /// Only the first and last dash-separated fields are considered, and only if they read like a
    /// person: no numbers, no format or series words, nothing from the wanted book's own details.
    /// </remarks>
    internal static partial class ReleaseAuthorReader
    {
        public static string? FindNamedAuthor(string foldedName, HashSet<string> ownWords)
        {
            var name = foldedName;
            for (var stripped = TrailingBracketPattern().Replace(name, string.Empty); stripped != name; stripped = TrailingBracketPattern().Replace(name, string.Empty))
            {
                name = stripped;
            }

            var fields = name.Split(" - ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (fields.Length < 2)
            {
                return null;
            }

            if (ReadsLikeAPerson(fields[^1], 1, 8, ownWords))
            {
                return fields[^1];
            }

            return ReadsLikeAPerson(fields[0], 2, 4, ownWords) ? fields[0] : null;
        }

        private static bool ReadsLikeAPerson(string field, int fewest, int most, HashSet<string> ownWords)
        {
            var words = ReleaseNameTokenizer.Tokenize(field).Where(token => token.Text != "and").ToList();
            return words.Count >= fewest && words.Count <= most && words.All(token =>
                !token.IsNumber
                && token.Text.All(char.IsLetter)
                && !ReleaseNameVocabulary.Stopwords.Contains(token.Text)
                && !ReleaseNameVocabulary.SeriesNouns.Contains(token.Text)
                && (token.Text == "various" || !ReleaseNameVocabulary.Noise.Contains(token.Text))
                && !ownWords.Any(own => ReleaseRelevanceEvaluator.SameWord(own, token.Text)));
        }

        [GeneratedRegex(@"\s*[\[\(\{][^\[\]\(\)\{\}]*[\]\)\}]\s*$")]
        private static partial Regex TrailingBracketPattern();
    }
}
