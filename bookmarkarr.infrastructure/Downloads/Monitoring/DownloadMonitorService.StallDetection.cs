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
using Microsoft.Extensions.Logging;

namespace Bookmarkarr.Infrastructure.Downloads.Monitoring
{
    public partial class DownloadMonitorProcessor
    {
        // Long enough that a slow or queued transfer is never called stalled, short enough that a
        // stuck queue is reported the same day rather than discovered by hand a week later.
        internal static readonly TimeSpan StallWarningThreshold = TimeSpan.FromHours(6);

        internal const string StallFingerprintKey = "StallFingerprint";
        internal const string StateChangedAtKey = DownloadStallState.StateChangedAtKey;
        internal const string StallWarnedAtKey = "StallWarnedAt";

        /// <summary>
        /// Notices a download whose state has stopped moving, and gives up on one the client has
        /// been calling stalled for hours.
        ///
        /// Every individual poll of a stuck download looks perfectly healthy — a status, a progress
        /// figure, no error — so nothing in the log ever says a transfer has gone nowhere for days.
        /// That silence is how a whole queue can sit at the same status indefinitely while the app
        /// reports no problem at all. Comparing each poll against the last state that actually
        /// differed turns "not moving" into something the log can say out loud.
        ///
        /// Reporting it was never enough on its own, though. An active download suppresses
        /// automatic search for its book, so a torrent with no seeds does not merely sit there —
        /// it holds the book hostage, and no amount of warning in the log releases it. Past
        /// <see cref="DownloadStallState.AbandonThreshold"/> the download is failed outright, which
        /// hands it to machinery that already exists: the failure counts a strike toward the
        /// blocklist, a second strike bars that release from being grabbed again, the book's search
        /// window is cleared, and the next pass picks the next best candidate. Nothing here needs
        /// to know about any of that; it only has to stop pretending the download is alive.
        ///
        /// The bookkeeping lives in metadata rather than a column so this needs no migration, and
        /// the warning repeats on the same interval so a long stall stays visible without filling
        /// the log on every polling cycle.
        /// </summary>
        internal void TrackProgressStall(DownloadClientConfiguration client, Download current, Download? previous)
        {
            // Import-owned and finished states are not "stalled" in any sense the user can act on;
            // their progress is deliberately frozen while another worker owns them.
            if (!DownloadStallState.CanStall(current.Status))
            {
                return;
            }

            var now = timeProvider.GetUtcNow().UtcDateTime;
            var fingerprint = $"{current.Status}:{decimal.Round(current.Progress, 2)}";

            if (previous == null || current.GetMetadataString(StallFingerprintKey) != fingerprint)
            {
                current.SetMetadata(StallFingerprintKey, fingerprint);
                current.SetMetadata(StateChangedAtKey, now.ToString("O", CultureInfo.InvariantCulture));
                current.Metadata.Remove(StallWarnedAtKey);
                return;
            }

            if (DownloadStallState.UnchangedFor(current, now) is not TimeSpan stalledFor)
            {
                // First poll after this code shipped, or a hand-edited row: start the clock now
                // rather than reporting a stall we have no evidence for.
                current.SetMetadata(StateChangedAtKey, now.ToString("O", CultureInfo.InvariantCulture));
                return;
            }

            // Checked before the warning threshold rather than under it, so the two durations stay
            // independent of each other and abandoning is never gated on a warning having fired.
            if (stalledFor >= DownloadStallState.AbandonThreshold
                && DownloadStallState.IsClientReportingStall(current.GetMetadataString(DownloadStallState.ClientStateKey)))
            {
                AbandonStalledDownload(client, current, stalledFor);
                return;
            }

            if (stalledFor < StallWarningThreshold)
            {
                return;
            }

            if (DownloadStallState.TryReadTimestamp(current, StallWarnedAtKey, out var warnedAt)
                && now - warnedAt < StallWarningThreshold)
            {
                return;
            }

            current.SetMetadata(StallWarnedAtKey, now.ToString("O", CultureInfo.InvariantCulture));
            logger.LogWarning(
                "Download {DownloadId} '{Title}' on client {ClientName} has not changed in {StalledHours:F1}h " +
                "(status {Status}, progress {Progress:F1}%). It will not import or be searched again while it stays active.",
                current.Id,
                current.Title,
                client.Name ?? client.Id ?? client.Type,
                stalledFor.TotalHours,
                current.Status,
                current.Progress);
        }

        /// <summary>
        /// Fails a download the client has been reporting as stalled for hours.
        /// </summary>
        /// <remarks>
        /// Only the status is set here. Persisting it is what triggers everything else — the
        /// blocklist strike in <c>DownloadService.UpdateAsync</c>, and the client cleanup and
        /// re-search in <c>OnDownloadFailed</c> — because the monitor calls this immediately
        /// before saving the row and comparing it against its previous state.
        /// </remarks>
        private void AbandonStalledDownload(DownloadClientConfiguration client, Download current, TimeSpan stalledFor)
        {
            var clientState = current.GetMetadataString(DownloadStallState.ClientStateKey) ?? "stalled";
            var reason = string.Format(
                CultureInfo.InvariantCulture,
                "Stalled in the download client for {0:F1}h with no progress (client state: {1})",
                stalledFor.TotalHours,
                clientState);

            current.Failed(reason);

            logger.LogWarning(
                "Giving up on download {DownloadId} '{Title}' on client {ClientName}: {Reason}. " +
                "The release counts a failure so the book can be searched again.",
                current.Id,
                current.Title,
                client.Name ?? client.Id ?? client.Type,
                reason);
        }
    }
}
