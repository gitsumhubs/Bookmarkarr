/*
 * Bookmarkarr - Audiobook Management System
 * Copyright (C) 2024-2026 Bookmarkarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using System.Text.RegularExpressions;

namespace Bookmarkarr.Application.Downloads.Queue
{
    internal static partial class DownloadSearchQueryBuilder
    {
        public static string Build(Audiobook audiobook) =>
            BuildCandidates(audiobook).FirstOrDefault() ?? string.Empty;

        /// <summary>
        /// Builds an ordered list of progressively broader queries to try in turn.
        /// </summary>
        /// <remarks>
        /// Catalogue titles - Goodreads ones especially - carry decorations that indexers
        /// do not have: series names in parentheses or brackets, "Book 3" / "#3" numbering,
        /// subtitles after a colon, and smart quotes. A single literal query built from
        /// those returns nothing, which is why an automatic search could find zero results
        /// for a book a broader manual search finds immediately.
        ///
        /// The ladder goes from most specific to least, so a precise match still wins when
        /// one exists and only genuinely empty searches fall through to a broader attempt:
        ///   1. normalized title + author
        ///   2. title with series/parenthetical decoration removed + author
        ///   3. normalized title alone
        ///
        /// It deliberately stops there. Dropping the title or querying by author alone
        /// would return unrelated books, and media filtering is enforced separately and
        /// server-side regardless of which query produced a result.
        /// </remarks>
        public static IReadOnlyList<string> BuildCandidates(Audiobook audiobook)
        {
            var author = audiobook.Authors?.FirstOrDefault();
            var normalizedTitle = NormalizeTitle(audiobook.Title);

            // Decoration must be removed while the brackets are still intact, because
            // normalizing flattens them to spaces and there would be nothing left to match.
            var strippedTitle = NormalizeTitle(StripSeriesDecoration(audiobook.Title));

            // A LinkedHashSet-style dedupe: preserves ladder order and drops rungs that
            // collapse to the same string for titles with no decoration.
            var candidates = new List<string>();
            void Add(params string?[] parts)
            {
                var query = string.Join(" ", parts.Where(part => !string.IsNullOrWhiteSpace(part))).Trim();
                if (query.Length > 0 && !candidates.Contains(query, StringComparer.OrdinalIgnoreCase))
                {
                    candidates.Add(query);
                }
            }

            Add(normalizedTitle, author);
            Add(strippedTitle, author);
            Add(normalizedTitle);
            Add(strippedTitle);

            return candidates;
        }

        /// <summary>
        /// Flattens punctuation that indexers tokenize differently than catalogues do.
        /// </summary>
        private static string NormalizeTitle(string? title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return string.Empty;
            }

            // Smart quotes and dashes routinely differ between a catalogue title and a
            // release name, so fold them to their ASCII equivalents before querying.
            var normalized = title
                .Replace('‘', '\'').Replace('’', '\'')
                .Replace('“', '"').Replace('”', '"')
                .Replace('–', '-').Replace('—', '-');

            normalized = PunctuationPattern().Replace(normalized, " ");
            return CollapseWhitespacePattern().Replace(normalized, " ").Trim();
        }

        /// <summary>
        /// Removes series/subtitle decoration such as "(Wheel of Time #3)" from a raw title.
        /// </summary>
        private static string StripSeriesDecoration(string? title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return string.Empty;
            }

            var stripped = ParentheticalPattern().Replace(title, " ");
            stripped = SeriesNumberPattern().Replace(stripped, " ");
            stripped = CollapseWhitespacePattern().Replace(stripped, " ").Trim(' ', '-', ':', ',');

            // Never strip the title down to nothing; if decoration was the whole title,
            // the original form is still the better query.
            return stripped.Length > 0 ? stripped : title;
        }

        [GeneratedRegex(@"[\[\]\(\)\{\}:;,""'`~!?*/\\|]+")]
        private static partial Regex PunctuationPattern();

        [GeneratedRegex(@"\s{2,}")]
        private static partial Regex CollapseWhitespacePattern();

        // Runs against the raw title, while the brackets are still present.
        [GeneratedRegex(@"[\(\[][^\)\]]*[\)\]]")]
        private static partial Regex ParentheticalPattern();

        // "#3" is matched separately from the word forms: '#' is not a word character, so
        // a leading \b would never match it after a space.
        [GeneratedRegex(@"(?:\b(?:book|volume|vol|part)\b\s*\.?\s*\d+|#\s*\d+)", RegexOptions.IgnoreCase)]
        private static partial Regex SeriesNumberPattern();
    }
}
