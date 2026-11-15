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

namespace Bookmarkarr.Application.Search.Contracts
{
    /// <summary>Which indexers a search goes to.</summary>
    public enum IndexerProtocolFilter
    {
        Any = 0,
        Usenet = 1,
        Torrent = 2
    }

    /// <summary>An indexer that could not be asked, or could not answer.</summary>
    public sealed record IndexerUnavailability(string IndexerName, string Reason, bool IsRateLimited, TimeSpan? RetryAfter);

    /// <summary>
    /// What a search across indexers came to: the results, and which indexers did not answer.
    /// </summary>
    /// <remarks>
    /// A plain result list cannot say the second part. An indexer that refused, or whose budget
    /// was spent, contributed an empty list like one that had nothing, so the caller walked the
    /// rest of its query ladder against a refusing site and then recorded the book as searched.
    /// </remarks>
    public sealed record IndexerSearchOutcome(
        List<IndexerSearchResult> Results,
        IReadOnlyList<IndexerUnavailability> Unavailable,
        int IndexersAsked)
    {
        public static IndexerSearchOutcome Empty { get; } = new([], [], 0);

        /// <summary>Every indexer asked was unavailable, so "no results" means nothing.</summary>
        public bool NobodyAnswered => IndexersAsked > 0 && Unavailable.Count >= IndexersAsked;
    }
}
