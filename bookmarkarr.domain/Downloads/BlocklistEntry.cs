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

namespace Bookmarkarr.Domain.Downloads
{
    /// <summary>
    /// A release that failed to download often enough that automatic search should stop
    /// picking it.
    ///
    /// Scope is deliberately one book, not the whole library: the same release name from
    /// the same group can be fine for a different title, and a global ban would punish an
    /// entire indexer for one bad post.
    /// </summary>
    public class BlocklistEntry
    {
        /// <summary>
        /// Two strikes. One failure is routine — a stalled peer, a transient client error,
        /// a node that dropped mid-transfer — and blocking on it would discard releases
        /// that succeed on a plain retry. A second failure of the same release is a
        /// pattern.
        /// </summary>
        public const int FailureThreshold = 2;

        public int Id { get; set; }

        /// <summary>Book this release was grabbed for. A blocklist is per-book, never global.</summary>
        public int? AudiobookId { get; set; }

        /// <summary>Edition the grab targeted, when the download recorded one.</summary>
        public int? EditionId { get; set; }

        /// <summary>Indexer-supplied release GUID. Null for indexers that do not expose one.</summary>
        public string? ReleaseGuid { get; set; }

        /// <summary>Indexer the release came from, so the same name on another indexer stays eligible.</summary>
        public int? IndexerId { get; set; }

        /// <summary>Release name exactly as the indexer reported it, for display.</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// <see cref="ReleaseIdentity.NormalizeReleaseName"/> of <see cref="Title"/>, stored
        /// so matching does not re-normalize on every search result comparison.
        /// </summary>
        public string NormalizedTitle { get; set; } = string.Empty;

        /// <summary>Reported size in bytes, the tiebreaker when no GUID is available.</summary>
        public long Size { get; set; }

        /// <summary>Indexer display name, shown in the UI so the entry is recognisable.</summary>
        public string? Source { get; set; }

        /// <summary>Torrent, Usenet or DirectDownload, for display.</summary>
        public string? Protocol { get; set; }

        /// <summary>How many failed grabs of this release were seen when it was blocklisted.</summary>
        public int FailureCount { get; set; }

        /// <summary>Client-reported failure text from the grab that tripped the threshold.</summary>
        public string? Reason { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// True when this entry describes the same release as the supplied candidate.
        /// </summary>
        public bool Matches(string? candidateGuid, int? candidateIndexerId, string? candidateTitle, long candidateSize) =>
            ReleaseIdentity.Matches(
                ReleaseGuid,
                IndexerId,
                NormalizedTitle,
                Size,
                candidateGuid,
                candidateIndexerId,
                candidateTitle,
                candidateSize);
    }
}
