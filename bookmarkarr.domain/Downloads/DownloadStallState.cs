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

namespace Bookmarkarr.Domain.Downloads
{
    /// <summary>
    /// One definition of what "stalled" means, shared by the monitor that acts on it and the API
    /// that reports it.
    ///
    /// Stalling is deliberately *not* a <see cref="DownloadStatus"/>. The client's own label is a
    /// momentary reading — qBittorrent (and so RDT Client's qBittorrent shim) reports
    /// <c>stalledDL</c> whenever the download rate is currently zero, which is routinely true for
    /// a healthy torrent between pieces, during a DHT lookup, or while it connects to peers. A
    /// status flipping in and out on that reading would be wrong often enough that people learn to
    /// ignore it, and every one of the several dozen places that switch on download status would
    /// have to learn about the new member to keep behaving correctly. Derived from the client
    /// label plus a duration, it stays a display concern and a monitor decision, and the status
    /// enum keeps meaning what it always meant.
    ///
    /// Two durations, because the two questions have different costs. Saying so in the UI is cheap
    /// and reverses itself the moment bytes move again; abandoning the release costs a grab and a
    /// strike against it, so it waits far longer for the answer to be unambiguous.
    /// </summary>
    public static class DownloadStallState
    {
        /// <summary>The client's own state string, recorded verbatim by the queue converter.</summary>
        public const string ClientStateKey = "ClientState";

        /// <summary>When this download's status and progress were last seen to actually differ.</summary>
        public const string StateChangedAtKey = "StateChangedAt";

        /// <summary>
        /// How long the client must keep reporting a stall before it is worth showing. Long enough
        /// that the ordinary gaps in a healthy torrent pass unremarked, short enough that someone
        /// watching a download that is going nowhere is told while they are still watching.
        /// </summary>
        public static readonly TimeSpan ReportingThreshold = TimeSpan.FromMinutes(10);

        /// <summary>
        /// How long a stall must last before the release is given up on. Matches the interval the
        /// monitor already warns on, and is deliberately far past <see cref="ReportingThreshold"/>:
        /// a torrent can sit without a peer for hours and still finish, so this is the point where
        /// waiting longer costs more than trying a different release.
        /// </summary>
        public static readonly TimeSpan AbandonThreshold = TimeSpan.FromHours(6);

        /// <summary>
        /// Whether this status is one a stall can even be measured against.
        ///
        /// Terminal and import-owned states have deliberately frozen progress — another worker owns
        /// them, and nothing about them is stuck in a way the user can act on.
        /// </summary>
        public static bool CanStall(DownloadStatus status) => status is not (
            DownloadStatus.Completed
            or DownloadStatus.Moved
            or DownloadStatus.Failed
            or DownloadStatus.Processing
            or DownloadStatus.ImportPending
            or DownloadStatus.ImportBlocked
            or DownloadStatus.ImportNameMismatch
            or DownloadStatus.SourceMissing);

        /// <summary>
        /// Whether the client is calling this download stalled right now.
        ///
        /// <c>stalledUP</c> is excluded on purpose: that is a finished torrent with nobody asking
        /// for it, which is a seeding condition rather than a fault, and treating it as a stall
        /// would abandon downloads that have already delivered their bytes.
        /// </summary>
        public static bool IsClientReportingStall(string? clientState)
        {
            if (string.IsNullOrWhiteSpace(clientState))
            {
                return false;
            }

            var normalized = clientState.Trim().ToLowerInvariant();
            return normalized is "stalleddl" or "stalled";
        }

        /// <summary>
        /// How long this download has gone without its status or progress changing, or null when
        /// the monitor has not recorded a reading to compare against yet.
        /// </summary>
        public static TimeSpan? UnchangedFor(Download download, DateTime utcNow)
        {
            ArgumentNullException.ThrowIfNull(download);

            if (!TryReadTimestamp(download, StateChangedAtKey, out var changedAt))
            {
                return null;
            }

            var elapsed = utcNow - changedAt;
            return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
        }

        /// <summary>
        /// Whether this download counts as stalled: its client says so, and has said so for at
        /// least <paramref name="minimumDuration"/> without the download moving.
        /// </summary>
        public static bool IsStalled(Download download, DateTime utcNow, TimeSpan minimumDuration)
        {
            ArgumentNullException.ThrowIfNull(download);

            if (!CanStall(download.Status))
            {
                return false;
            }

            if (!IsClientReportingStall(download.GetMetadataString(ClientStateKey)))
            {
                return false;
            }

            return UnchangedFor(download, utcNow) is TimeSpan unchanged && unchanged >= minimumDuration;
        }

        /// <summary>
        /// Reads one of the stall timestamps back out of metadata.
        /// </summary>
        /// <remarks>
        /// Written with "O" against a UTC DateTime, so the stored text carries its own offset and
        /// RoundtripKind is the correct style. It cannot be combined with AdjustToUniversal —
        /// DateTime.TryParse throws on that pair — so the conversion happens after parsing.
        /// </remarks>
        public static bool TryReadTimestamp(Download download, string key, out DateTime value)
        {
            ArgumentNullException.ThrowIfNull(download);

            var raw = download.GetMetadataString(key);
            if (!DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value))
            {
                return false;
            }

            value = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
            return true;
        }
    }
}
