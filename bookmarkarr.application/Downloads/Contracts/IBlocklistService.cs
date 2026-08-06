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

namespace Bookmarkarr.Application.Downloads.Contracts
{
    public interface IBlocklistService
    {
        /// <summary>
        /// Records a failed download and blocklists the release once it has failed
        /// <see cref="BlocklistEntry.FailureThreshold"/> times for that book.
        /// </summary>
        /// <returns>The entry when the release is now blocklisted, otherwise null.</returns>
        Task<BlocklistEntry?> RecordFailureAsync(Download download, CancellationToken ct = default);

        /// <summary>
        /// Blocklists a release immediately, without waiting for
        /// <see cref="BlocklistEntry.FailureThreshold"/> failures.
        ///
        /// For callers outside Bookmarkarr that have already established the release is bad —
        /// a cleanup daemon that watched a torrent sit at zero bytes across several checks
        /// knows more than a single client status poll does, so making it earn a second strike
        /// would just mean grabbing the same dead release again.
        /// </summary>
        /// <returns>The new or pre-existing entry, or null when the download has no book.</returns>
        Task<BlocklistEntry?> BlocklistReleaseAsync(Download download, string reason, CancellationToken ct = default);

        Task<IReadOnlyList<BlocklistEntry>> GetForAudiobookAsync(int audiobookId, CancellationToken ct = default);
    }
}
