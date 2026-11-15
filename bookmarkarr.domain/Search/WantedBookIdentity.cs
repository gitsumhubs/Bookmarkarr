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
    /// <summary>The book a search is for, as the catalogue describes it.</summary>
    public sealed record WantedBook
    {
        public string? Title { get; init; }
        public string? Subtitle { get; init; }
        public IReadOnlyList<string> Authors { get; init; } = [];
        public string? Series { get; init; }
        public string? SeriesNumber { get; init; }
        public IReadOnlyList<string> Narrators { get; init; } = [];

        public static WantedBook From(Bookmarkarr.Domain.Audiobooks.Audiobook book) => new()
        {
            Title = book.Title,
            Subtitle = book.Subtitle,
            Authors = book.Authors ?? [],
            Series = book.Series,
            SeriesNumber = book.SeriesNumber,
            Narrators = book.Narrators ?? []
        };
    }

    /// <summary>
    /// A wanted book broken into what a release name can be checked against.
    /// </summary>
    /// <remarks>
    /// Catalogue titles are not clean. Goodreads writes "Night Watch (Discworld, #29; City Watch,
    /// #6)", Audible writes "The Sandman: Act II" and puts "Dungeon Crawler Carl, Book 3" in the
    /// subtitle. The series and the volume number are recovered from wherever they were written,
    /// and the title is left as the words a release has to contain.
    /// </remarks>
    internal sealed partial class WantedBookIdentity
    {
        private static readonly HashSet<string> Articles = new(StringComparer.Ordinal) { "the", "a", "an" };
        private static readonly HashSet<string> NameSuffixes = new(StringComparer.Ordinal) { "jr", "sr", "2", "3", "phd", "md" };

        private WantedBookIdentity()
        {
        }

        /// <summary>Title parts split at colons and dashes; every part has to appear in a release.</summary>
        public List<string[]> TitleSegments { get; } = [];
        public List<string[]> SeriesNames { get; } = [];
        public HashSet<decimal> Numbers { get; } = [];
        public HashSet<string> AuthorSurnames { get; } = new(StringComparer.Ordinal);

        /// <summary>Every word the catalogue itself uses for this book: never a sign of another book.</summary>
        public HashSet<string> OwnWords { get; } = new(StringComparer.Ordinal);
        public bool HasAuthors => AuthorSurnames.Count > 0;

        /// <summary>The whole title is also the series name ("Dungeon Crawler Carl").</summary>
        public bool TitleIsSeriesLabel { get; private set; }

        /// <summary>The part before the colon is the series name ("Mistborn: The Final Empire").</summary>
        public bool FirstSegmentIsSeriesLabel { get; private set; }

        /// <summary>The title itself ends in a volume ("The Sandman: Act II").</summary>
        public bool TitleCarriesVolume { get; private set; }

        /// <summary>
        /// Nothing but the number tells this book from its siblings, and it is not the first, so
        /// a release that gives no number cannot be taken for it. "The Sandman" is Act I.
        /// </summary>
        public bool NeedsStatedVolume => (TitleCarriesVolume || TitleIsSeriesLabel) && Numbers.Count > 0 && !Numbers.Contains(1);

        public static WantedBookIdentity From(WantedBook book)
        {
            var identity = new WantedBookIdentity();
            var title = book.Title ?? string.Empty;

            foreach (Match group in BracketGroupPattern().Matches(title))
            {
                foreach (var part in group.Groups[1].Value.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    identity.ReadSeriesNote(part);
                }
            }

            identity.ReadSeriesNote(book.Subtitle);
            identity.AddSeries(book.Series);
            identity.AddNumber(book.SeriesNumber);

            var coreTitle = BracketGroupPattern().Replace(title, " ");
            foreach (var segment in ReleaseNameTokenizer.Tokenize(coreTitle).GroupBy(token => token.Segment))
            {
                var words = segment.Select(token => token.Text).ToArray();
                identity.OwnWords.UnionWith(words);

                // "Act II" on its own is this book's place in the series, not part of its name.
                if (words.Length == 2 && ReleaseNameTokenizer.VolumeMarkers.Contains(words[0])
                    && decimal.TryParse(words[1], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var volume))
                {
                    identity.Numbers.Add(volume);
                    identity.TitleCarriesVolume = true;
                    continue;
                }

                words = WithoutLeadingArticle(words);
                if (words.Length > 0)
                {
                    identity.TitleSegments.Add(words);
                }
            }

            foreach (var name in book.Authors.Where(name => !string.IsNullOrWhiteSpace(name)))
            {
                var words = ReleaseNameTokenizer.Tokenize(name).Select(token => token.Text).ToList();
                identity.OwnWords.UnionWith(words);
                var surname = words.LastOrDefault(word => !NameSuffixes.Contains(word));
                if (surname != null)
                {
                    identity.AuthorSurnames.Add(surname);
                }
            }

            foreach (var name in book.Narrators)
            {
                identity.OwnWords.UnionWith(ReleaseNameTokenizer.Tokenize(name).Select(token => token.Text));
            }

            if (identity.TitleSegments.Count > 0)
            {
                var whole = identity.TitleSegments.SelectMany(words => words).ToArray();
                identity.TitleIsSeriesLabel = identity.SeriesNames.Any(series => series.SequenceEqual(whole));
                identity.FirstSegmentIsSeriesLabel = identity.TitleSegments.Count > 1
                    && identity.SeriesNames.Any(series => series.SequenceEqual(identity.TitleSegments[0]));

                // A book named exactly like its series is the one that started it.
                if (identity.TitleIsSeriesLabel && identity.Numbers.Count == 0)
                {
                    identity.Numbers.Add(1);
                }
            }

            return identity;
        }

        public static string[] WithoutLeadingArticle(string[] words) =>
            words.Length > 1 && Articles.Contains(words[0]) ? words[1..] : words;

        /// <summary>Reads "Discworld, #29", "Glass Immortals #1" or "Path of the Berserker, Book 1".</summary>
        private void ReadSeriesNote(string? note)
        {
            if (string.IsNullOrWhiteSpace(note))
            {
                return;
            }

            OwnWords.UnionWith(ReleaseNameTokenizer.Tokenize(note).Select(token => token.Text));
            var match = SeriesNotePattern().Match(note);
            if (match.Success)
            {
                AddSeries(match.Groups["name"].Value);
                AddNumber(match.Groups["number"].Value);
            }
        }

        private void AddSeries(string? series)
        {
            if (string.IsNullOrWhiteSpace(series))
            {
                return;
            }

            var words = WithoutLeadingArticle(
                ReleaseNameTokenizer.Tokenize(BracketGroupPattern().Replace(series, " ")).Select(token => token.Text).ToArray());
            OwnWords.UnionWith(words);
            if (words.Length > 0 && !SeriesNames.Any(existing => existing.SequenceEqual(words)))
            {
                SeriesNames.Add(words);
            }
        }

        private void AddNumber(string? value)
        {
            if (decimal.TryParse(value?.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
            {
                Numbers.Add(number);
            }
        }

        [GeneratedRegex(@"[\(\[]([^\(\)\[\]]*)[\)\]]")]
        private static partial Regex BracketGroupPattern();

        [GeneratedRegex(@"^\s*(?<name>.+?)[\s,]*(?:#\s*|\b(?:book|vol\.?|volume)\s+)(?<number>\d+(?:\.\d+)?)\b", RegexOptions.IgnoreCase)]
        private static partial Regex SeriesNotePattern();
    }
}
