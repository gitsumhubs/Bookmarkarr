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

using System.Globalization;
using System.Text.RegularExpressions;

namespace Bookmarkarr.Domain.Search
{
    /// <summary>
    /// Spots a release that holds several books, or only a piece of one, from its name.
    /// </summary>
    /// <remarks>
    /// A pack that contains the wanted book is still refused. Taken unattended it is filed as that
    /// one book: "He Who Fights With Monsters, Books 1-12" became twelve books under one title.
    /// A word the wanted book uses itself ("The Complete Collection") is never held against it.
    /// </remarks>
    internal static partial class ReleasePackDetector
    {
        private static readonly HashSet<string> PackNouns = new(StringComparer.Ordinal)
        {
            "trilogy", "series", "saga", "collection", "omnibus", "books", "duology", "quartet", "quintet", "novels", "boxset"
        };

        /// <summary>A phrase in the release name that means more than one book, or null.</summary>
        public static string? FindPackMarker(string foldedName, HashSet<string> ownWords)
        {
            foreach (var pattern in new[] { LooseRangePattern(), TightRangePattern(), SpelledRangePattern() })
            {
                foreach (Match match in pattern.Matches(foldedName))
                {
                    var first = decimal.Parse(match.Groups["first"].Value, CultureInfo.InvariantCulture);
                    var last = decimal.Parse(match.Groups["last"].Value, CultureInfo.InvariantCulture);
                    if (last > first)
                    {
                        return match.Value.Trim();
                    }
                }
            }

            foreach (Match match in PackWordPattern().Matches(foldedName))
            {
                var headWord = match.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).First(word => !char.IsAsciiDigit(word[0]));
                if (!ownWords.Contains(headWord))
                {
                    return match.Value.Trim();
                }
            }

            return null;
        }

        /// <summary>"Ready Player One and Two", "Mistborn Trilogy": the title runs on into more books.</summary>
        public static string? FindPackAfterTitle(List<NameToken> tokens, int titleEnd, HashSet<string> ownWords)
        {
            if (titleEnd + 1 >= tokens.Count)
            {
                return null;
            }

            var next = tokens[titleEnd + 1];
            if (PackNouns.Contains(next.Text) && !ownWords.Contains(next.Text))
            {
                return next.Text;
            }

            if (next.Text is "and" or "plus" && titleEnd + 2 < tokens.Count)
            {
                var counted = tokens[titleEnd + 2];
                if ((counted.IsNumber && !counted.IsYear) || ReleaseNameTokenizer.IsNumberWord(counted.Text))
                {
                    return $"{next.Text} {counted.Text}";
                }
            }

            return null;
        }

        /// <summary>"(1 of 2)", "part 2 of 3": a release that is one piece of the book.</summary>
        public static string? FindPartialMarker(string foldedName)
        {
            foreach (Match match in PartOfPattern().Matches(foldedName))
            {
                var piece = int.Parse(match.Groups["piece"].Value, CultureInfo.InvariantCulture);
                var total = int.Parse(match.Groups["total"].Value, CultureInfo.InvariantCulture);
                if (total >= 2 && piece <= total)
                {
                    return match.Value.Trim();
                }
            }

            return null;
        }

        // "Books 1-12", "Books 01 - 09", "Seasons 1 to 3", "Volumes 1, 2": a plural says it outright.
        [GeneratedRegex(@"\b(?:books|bks|vols|volumes|seasons|novels)\s*#?\s*(?<first>\d{1,3})\s*(?:-|to|thru|through|and|,|\+)\s*#?\s*(?<last>\d{1,3})\b")]
        private static partial Regex LooseRangePattern();

        // "Season 1-3", "#1-3". With a singular the range has to be tight: "Book 1 - 64 kbps" is not one.
        [GeneratedRegex(@"(?:\b(?:book|bk|vol|volume|season|series)\s*#?\s*|#)(?<first>\d{1,3})-(?<last>\d{1,3})\b")]
        private static partial Regex TightRangePattern();

        [GeneratedRegex(@"\b(?:book|bk|vol|volume|season)\s*#?\s*(?<first>\d{1,3})\s+(?:to|thru|through|and)\s+#?\s*(?<last>\d{1,3})\b")]
        private static partial Regex SpelledRangePattern();

        [GeneratedRegex(@"\b(?:collection|collected works|box ?set|boxed set|omnibus|bundle|megapack|complete (?:series|saga|works|trilogy|set|audiobooks?)|entire (?:series|saga|collection|set|audiobooks?)|all \d{1,3} books|\d{1,3} books|\d{1,3} book (?:series|set))\b")]
        private static partial Regex PackWordPattern();

        // "Book 1 of 3" is a volume in a series, so a volume word in front rules it out.
        [GeneratedRegex(@"(?<!\b(?:book|bk|vol|volume|season)\s+)\b(?<piece>\d{1,2})\s+of\s+(?<total>\d{1,2})\b")]
        private static partial Regex PartOfPattern();
    }
}
