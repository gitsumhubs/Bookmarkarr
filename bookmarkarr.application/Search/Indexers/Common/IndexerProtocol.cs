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

namespace Bookmarkarr.Application.Search.Indexers.Common;

/// <summary>
/// Answers whether an indexer speaks to a torrent site, which decides whether searching it is
/// paced by <see cref="ISearchThrottle"/>.
/// </summary>
public static class IndexerProtocol
{
    /// <summary>
    /// True for indexers backed by a torrent site. <see cref="Indexer.Type"/> is authoritative when
    /// set — it is what the Prowlarr import writes from the indexer's own protocol — and the
    /// implementation name answers for records old or hand-made enough to have left it blank.
    /// </summary>
    public static bool IsTorrent(Indexer indexer)
    {
        if (!string.IsNullOrWhiteSpace(indexer.Type))
        {
            return indexer.Type.Equals("Torrent", StringComparison.OrdinalIgnoreCase);
        }

        return indexer.Implementation.Equals("Torznab", StringComparison.OrdinalIgnoreCase)
            || indexer.Implementation.Equals("MyAnonamouse", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when any of <paramref name="indexers"/> is a torrent indexer.</summary>
    public static bool AnyTorrent(IEnumerable<Indexer> indexers) => indexers.Any(IsTorrent);
}
