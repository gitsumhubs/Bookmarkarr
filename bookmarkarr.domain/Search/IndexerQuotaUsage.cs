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

namespace Bookmarkarr.Domain.Search
{
    /// <summary>
    /// One request Bookmarkarr made to an indexer, recorded so a rolling hourly budget can be
    /// enforced across searches and grabs alike.
    /// </summary>
    /// <remarks>
    /// Stored rather than counted in memory because the budget exists to protect a remote site that
    /// bans by IP: a restart must not hand the process a fresh allowance the site does not honour.
    /// Rows older than the window are pruned, so this stays small.
    /// </remarks>
    public class IndexerQuotaUsage
    {
        public int Id { get; set; }

        public int IndexerId { get; set; }

        /// <summary>When the request was made. UTC.</summary>
        public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// What spent the budget. Kept so the interactive reserve can be enforced and so an
        /// operator can see whether automation or their own clicking used the window up.
        /// </summary>
        public IndexerQuotaPurpose Purpose { get; set; }
    }

    /// <summary>What a request to an indexer was for.</summary>
    public enum IndexerQuotaPurpose
    {
        /// <summary>Unattended search. Yields to the interactive reserve.</summary>
        AutomaticSearch = 0,

        /// <summary>A search a person asked for and is waiting on.</summary>
        InteractiveSearch = 1,

        /// <summary>Fetching a release from the indexer, which is another hit on the site.</summary>
        Grab = 2
    }
}
