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
    /// The one place a release's language is read from an indexer field or guessed from its name.
    /// </summary>
    /// <remarks>
    /// There used to be four copies of this, and each treated any standalone two-letter code as a
    /// language whatever its case. A hyphen is a word boundary, so "Cul-de-Sac Carnage" carried a
    /// standalone "de" and was reported as German, "Tour de France" likewise, and the usenet scorer
    /// went further and matched "es", "de" and "fr" as substrings of any word. Each wrong answer
    /// cost an English release the language-mismatch penalty.
    ///
    /// The rule now follows how release names are actually written. A language tag is either
    /// inside brackets or written in capitals; a lower-case "de" in running text is a word.
    /// </remarks>
    public static partial class ReleaseLanguageDetector
    {
        private static readonly Dictionary<string, string> Codes = new(StringComparer.OrdinalIgnoreCase)
        {
            { "ENG", "English" }, { "EN", "English" },
            { "DUT", "Dutch" }, { "NLD", "Dutch" }, { "NL", "Dutch" },
            { "GER", "German" }, { "DEU", "German" }, { "DE", "German" },
            { "FRE", "French" }, { "FRA", "French" }, { "FR", "French" },
            { "SPA", "Spanish" }, { "ES", "Spanish" }
        };

        private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
        {
            { "English", "English" },
            { "German", "German" }, { "Deutsch", "German" },
            { "French", "French" },
            { "Dutch", "Dutch" },
            { "Spanish", "Spanish" },
            // Read only inside brackets, where AudioBook Bay's "Language:" line arrives as
            // "[Italian]". Not in the usenet whole-word pattern, and words that are also ordinary
            // English ("Polish", "Chinese") are left out so a bracketed title cannot trip them.
            { "Italian", "Italian" }, { "Portuguese", "Portuguese" }, { "Russian", "Russian" },
            { "Swedish", "Swedish" }, { "Norwegian", "Norwegian" }, { "Danish", "Danish" },
            { "Finnish", "Finnish" }, { "Czech", "Czech" }, { "Hungarian", "Hungarian" }, { "Hindi", "Hindi" }
        };

        /// <summary>
        /// Reads a value that is known to be a language and nothing else, such as an indexer's
        /// language attribute. Case does not matter here: the whole value is the answer.
        /// </summary>
        public static string? FromCode(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            if (Codes.TryGetValue(trimmed, out var fromCode))
            {
                return fromCode;
            }

            return Names.TryGetValue(trimmed, out var fromName) ? fromName : null;
        }

        /// <summary>
        /// Reads an indexer's language field, which is usually a bare code but is sometimes a short
        /// tagged string such as "[ENG / M4B]".
        /// </summary>
        public static string? FromField(string? value) => FromCode(value) ?? FromText(value);

        /// <summary>
        /// Looks for a language tag in free text such as a release title or description.
        /// </summary>
        /// <remarks>
        /// Two forms count. Inside brackets, an element on its own ("(de)", "[eng / m4b]") is a tag
        /// in any case, while a word among other words must be capitalised as a code or be a
        /// language name, so "(de luxe edition)" is left alone. Outside brackets only a capitalised
        /// code standing by itself counts, and never one joined to its neighbours by a hyphen or an
        /// apostrophe.
        /// </remarks>
        public static string? FromText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            foreach (Match group in BracketGroupPattern().Matches(text))
            {
                var fromBracket = FromBracketContent(group.Groups[1].Value);
                if (fromBracket != null)
                {
                    return fromBracket;
                }
            }

            var bare = BareCodePattern().Match(text);
            return bare.Success && Codes.TryGetValue(bare.Value, out var language) ? language : null;
        }

        /// <summary>
        /// Looks for a language written out as a whole word: "German", "Deutsch", "English".
        /// </summary>
        /// <remarks>
        /// Kept apart from <see cref="FromText"/> because it is only a fair reading of a usenet
        /// release name, where the word is a tag. In a book title it is just a word — "The French
        /// Lieutenant's Woman" is not a French release.
        /// </remarks>
        public static string? FromLanguageName(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var match = LanguageNamePattern().Match(text);
            return match.Success && Names.TryGetValue(match.Value, out var language) ? language : null;
        }

        /// <summary>
        /// Guesses the language of a usenet release from its name: a tag first, then one of the
        /// caller's preferred languages written out, then any language name.
        /// </summary>
        /// <remarks>
        /// Whole words only. The scorer used to test for "es", "de" and "fr" as substrings, which
        /// made "James" Spanish and "hidden" German.
        ///
        /// Preferred entries of three characters or fewer are skipped in the middle step. They are
        /// codes, and codes are <see cref="FromText"/>'s business: as bare lower-case words they
        /// cannot be told apart from the text around them.
        /// </remarks>
        public static string? FromUsenetReleaseName(string? title, IEnumerable<string>? preferredLanguages)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            var tagged = FromText(title);
            if (tagged != null)
            {
                return tagged;
            }

            var preferred = (preferredLanguages ?? [])
                .Where(language => !string.IsNullOrWhiteSpace(language) && language.Trim().Length > 3)
                .FirstOrDefault(language => ContainsWholeWord(title, language.Trim()));

            return preferred ?? FromLanguageName(title);
        }

        /// <summary>
        /// Whether two language values mean the same language, so a profile that prefers "en" is
        /// satisfied by a release reported as "English".
        /// </summary>
        public static bool AreSameLanguage(string? left, string? right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            var canonicalLeft = FromCode(left) ?? left.Trim();
            var canonicalRight = FromCode(right) ?? right.Trim();
            return string.Equals(canonicalLeft, canonicalRight, StringComparison.OrdinalIgnoreCase);
        }

        private static string? FromBracketContent(string content)
        {
            foreach (var element in content.Split(ElementSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var whole = FromCode(element);
                if (whole != null)
                {
                    return whole;
                }

                foreach (var word in element.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (Names.TryGetValue(word, out var fromName))
                    {
                        return fromName;
                    }

                    if (IsUpperCase(word) && Codes.TryGetValue(word, out var fromCode))
                    {
                        return fromCode;
                    }
                }
            }

            return null;
        }

        private static bool ContainsWholeWord(string text, string word)
        {
            var index = text.IndexOf(word, StringComparison.OrdinalIgnoreCase);
            while (index >= 0)
            {
                var end = index + word.Length;
                var startsClean = index == 0 || !char.IsLetter(text[index - 1]);
                var endsClean = end == text.Length || !char.IsLetter(text[end]);
                if (startsClean && endsClean)
                {
                    return true;
                }

                index = text.IndexOf(word, index + 1, StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private static bool IsUpperCase(string word) => word.All(char.IsUpper);

        private static readonly char[] ElementSeparators = ['/', ',', '|', ';'];

        // Short on purpose: a tag block is a handful of tokens, not a bracketed sentence.
        [GeneratedRegex(@"[\[\(]([^\[\]\(\)]{1,40})[\]\)]")]
        private static partial Regex BracketGroupPattern();

        // Case-sensitive. The lookarounds refuse letters and digits (so "DEN" is not "DE") and
        // also hyphens and apostrophes, which join a word to its neighbours rather than separate it.
        [GeneratedRegex(@"(?<![\p{L}\p{N}'’\-])(?:ENG|EN|DUT|NLD|NL|GER|DEU|DE|FRE|FRA|FR|SPA|ES)(?![\p{L}\p{N}'’\-])")]
        private static partial Regex BareCodePattern();

        [GeneratedRegex(@"(?<!\p{L})(?:English|German|Deutsch|French|Dutch|Spanish)(?!\p{L})", RegexOptions.IgnoreCase)]
        private static partial Regex LanguageNamePattern();
    }
}
