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

            // Everything before the first colon. Catalogues carry the full
            // "Main Title: A Long Explanatory Subtitle"; indexers almost never do, so a query
            // built from the whole thing matches nothing while the main title alone matches
            // immediately.
            var mainTitle = NormalizeTitle(StripSubtitle(audiobook.Title));

            Add(normalizedTitle, author);
            Add(strippedTitle, author);
            Add(mainTitle, author);
            Add(normalizedTitle);
            Add(strippedTitle);
            Add(mainTitle);

            // Last resort: the single most distinctive word plus the author. Some indexers fail
            // on a multi-word phrase even when every word in it matches on its own — an ABB
            // release indexed as "The Butcher's Masquerade" returns nothing for either
            // "Butcher's Masquerade" or "Butchers Masquerade", but "Masquerade Matt Dinniman"
            // finds it immediately. Requires an author, because a bare word is far too broad.
            var distinctive = DistinctiveWord(normalizedTitle);
            if (!string.IsNullOrEmpty(distinctive) && !string.IsNullOrWhiteSpace(author))
            {
                // Guarded rather than left to Add(), which drops blank parts and would happily
                // emit the word alone, or — worse — the author alone when no word qualifies.
                Add(distinctive, author);
            }

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

            // Apostrophes are removed rather than spaced out, because they sit *inside* a word.
            // Spacing them turns "Carl's" into "Carl s", and indexers that tokenize on whitespace
            // then never match "Carls" — an automatic search returns zero results for a book the
            // manual path, which strips instead, finds immediately.
            normalized = IntraWordPunctuationPattern().Replace(normalized, string.Empty);

            normalized = PunctuationPattern().Replace(normalized, " ");
            return CollapseWhitespacePattern().Replace(normalized, " ").Trim();
        }

        /// <summary>
        /// Removes series/subtitle decoration such as "(Wheel of Time #3)" from a raw title.
        /// </summary>
        /// <summary>
        /// The longest non-trivial word in a title, as a proxy for the most distinctive one.
        /// </summary>
        /// <remarks>
        /// Length is a crude but effective proxy: it picks "Masquerade", "Horribles" and "Omens"
        /// over the surrounding filler. Returns empty when nothing is long enough to be worth
        /// querying alone, which drops the rung rather than issuing a hopelessly broad search.
        /// </remarks>
        private static string DistinctiveWord(string? normalizedTitle)
        {
            if (string.IsNullOrWhiteSpace(normalizedTitle))
            {
                return string.Empty;
            }

            var best = normalizedTitle
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(word => word.Length >= 5 && !StopWords.Contains(word))
                .OrderByDescending(word => word.Length)
                .FirstOrDefault();

            return best ?? string.Empty;
        }

        /// <summary>Common title words that carry no distinguishing weight.</summary>
        private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "audiobook", "unabridged", "abridged", "volume", "series", "novel", "chronicles",
            "complete", "edition", "trilogy", "collection"
        };

        /// <summary>
        /// Drops a subtitle introduced by a colon, keeping the main title.
        /// </summary>
        /// <remarks>
        /// Refuses to shorten to something too generic. "It: A Novel" would become "It", which
        /// matches everything and nothing; a main title has to be substantial enough to be worth
        /// querying on its own, or the original is returned and the rung dedupes away.
        /// </remarks>
        private static string StripSubtitle(string? title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return string.Empty;
            }

            var separator = title.IndexOf(':');
            if (separator <= 0)
            {
                return title;
            }

            var main = title[..separator].Trim(' ', '-', ',', ':');

            // Two words, or one long one. Anything shorter is too weak a query to be useful.
            var wordCount = main.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            return wordCount >= 2 || main.Length >= 8 ? main : title;
        }

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

        // Word-internal marks: dropped outright so the surrounding letters stay one token.
        [GeneratedRegex(@"['`\u2018\u2019]+")]
        private static partial Regex IntraWordPunctuationPattern();

        // Separators: flattened to a space, since they genuinely divide words.
        [GeneratedRegex(@"[\[\]\(\)\{\}:;,""~!?*/\\|]+")]
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
