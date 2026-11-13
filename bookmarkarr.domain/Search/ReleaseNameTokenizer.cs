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
using System.Text;

namespace Bookmarkarr.Domain.Search
{
    /// <summary>
    /// One word or number of a release name or book title, with where it sat.
    /// </summary>
    /// <param name="Text">Folded text: lower case, no accents, numbers in canonical form.</param>
    /// <param name="Segment">Rises at every separator, so words sharing a value were written together.</param>
    /// <param name="InBracket">Whether the word was inside any kind of bracket.</param>
    internal readonly record struct NameToken(string Text, int Segment, bool InBracket)
    {
        public bool IsNumber => Text.Length > 0 && char.IsAsciiDigit(Text[0]) && Number.HasValue;

        public decimal? Number =>
            decimal.TryParse(Text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) ? value : null;

        /// <summary>A four-digit year is a publication date, never a volume number.</summary>
        public bool IsYear => Number is >= 1900 and <= 2100 && Text.Length == 4;
    }

    /// <summary>
    /// Turns a release name or a catalogue title into comparable words.
    /// </summary>
    /// <remarks>
    /// Both sides of a comparison go through here, so the same spelling habits fold the same way:
    /// "Sorcerer’s" and "Sorcerers", "Half-Blood" and "Half Blood", "Act III" and "Act 3",
    /// "Book One" and "Book 1", "[01]" and "1", "The.Final.Empire" and "The Final Empire".
    /// </remarks>
    internal static class ReleaseNameTokenizer
    {
        private const string SegmentBreaks = ":,;|/!?~\"+=";

        private static readonly Dictionary<string, string> RomanNumerals = new(StringComparer.Ordinal)
        {
            { "ii", "2" }, { "iii", "3" }, { "iv", "4" }, { "vi", "6" }, { "vii", "7" }, { "viii", "8" }, { "ix", "9" }
        };

        private static readonly Dictionary<string, string> NumberWords = new(StringComparer.Ordinal)
        {
            { "one", "1" }, { "two", "2" }, { "three", "3" }, { "four", "4" }, { "five", "5" }, { "six", "6" },
            { "seven", "7" }, { "eight", "8" }, { "nine", "9" }, { "ten", "10" }, { "eleven", "11" }, { "twelve", "12" }
        };

        /// <summary>Words that introduce a volume number: "Book 3", "Vol. 2", "Season 1", "Act III".</summary>
        public static readonly HashSet<string> VolumeMarkers = new(StringComparer.Ordinal)
        {
            "book", "bk", "vol", "volume", "season", "act", "akt", "day", "number", "no"
        };

        public static bool IsNumberWord(string text) => NumberWords.ContainsKey(text);

        /// <summary>Lower case, accents removed, typographic quotes and dashes made plain, apostrophes dropped.</summary>
        public static string Fold(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            foreach (var character in value.Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                switch (character)
                {
                    // An apostrophe joins: "Sorcerer's" is written "Sorcerers" as often as not.
                    case '\'' or '’' or '‘' or '`' or '´':
                        break;
                    case '–' or '—' or '−':
                        builder.Append('-');
                        break;
                    case '“' or '”':
                        builder.Append('"');
                        break;
                    case '&':
                        builder.Append(" and ");
                        break;
                    default:
                        builder.Append(char.ToLowerInvariant(character));
                        break;
                }
            }

            return builder.ToString().Normalize(NormalizationForm.FormC);
        }

        public static List<NameToken> Tokenize(string? value)
        {
            var text = Fold(value);
            var tokens = new List<NameToken>();
            if (text.Length == 0)
            {
                return tokens;
            }

            // Scene-style names ("Author.Name-The.Title.2022") use dots for spaces and a bare hyphen
            // between fields. Everywhere else a bare hyphen sits inside a word ("Full-Cast").
            var dotted = !text.Any(char.IsWhiteSpace) && text.Count(c => c is '.' or '_') >= 2;
            var word = new StringBuilder();
            var segment = 0;
            var depth = 0;

            void Flush()
            {
                if (word.Length > 0)
                {
                    tokens.Add(new NameToken(word.ToString(), segment, depth > 0));
                    word.Clear();
                }
            }

            for (var i = 0; i < text.Length; i++)
            {
                var character = text[i];
                if (char.IsLetterOrDigit(character))
                {
                    word.Append(character);
                    continue;
                }

                if (character == '.' && IsDecimalPoint(text, i, word))
                {
                    word.Append(character);
                    continue;
                }

                Flush();
                if (character is '(' or '[' or '{')
                {
                    segment++;
                    depth++;
                }
                else if (character is ')' or ']' or '}')
                {
                    segment++;
                    depth = Math.Max(0, depth - 1);
                }
                else if (SegmentBreaks.Contains(character) || (character == '-' && (dotted || IsSpacedHyphen(text, i))))
                {
                    segment++;
                }
            }

            Flush();
            return Canonicalize(tokens);
        }

        private static bool IsDecimalPoint(string text, int index, StringBuilder word)
        {
            if (word.Length == 0 || word.Length > 2 || index + 1 >= text.Length)
            {
                return false;
            }

            for (var i = 0; i < word.Length; i++)
            {
                if (!char.IsAsciiDigit(word[i]))
                {
                    return false;
                }
            }

            // "2.5" and "2.0" are numbers; "2.2022" and "5.The" are a field break.
            var digitsAfter = 0;
            for (var i = index + 1; i < text.Length && char.IsAsciiDigit(text[i]); i++)
            {
                digitsAfter++;
            }

            var following = index + 1 + digitsAfter;
            return digitsAfter == 1 && (following >= text.Length || !char.IsLetter(text[following]));
        }

        private static bool IsSpacedHyphen(string text, int index)
        {
            var before = index == 0 || char.IsWhiteSpace(text[index - 1]);
            var after = index == text.Length - 1 || char.IsWhiteSpace(text[index + 1]);
            return before || after;
        }

        private static List<NameToken> Canonicalize(List<NameToken> tokens)
        {
            for (var i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                var text = token.Text;
                if (RomanNumerals.TryGetValue(text, out var roman))
                {
                    text = roman;
                }
                else if (i > 0 && VolumeMarkers.Contains(tokens[i - 1].Text) && NumberWords.TryGetValue(text, out var spelled))
                {
                    text = spelled;
                }
                else if (char.IsAsciiDigit(text[0])
                    && decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
                {
                    // "01", "1" and "1.0" are one value.
                    text = (number / 1.0000000000000000000000000000m).ToString(CultureInfo.InvariantCulture);
                }

                tokens[i] = token with { Text = text };
            }

            return tokens;
        }
    }
}
