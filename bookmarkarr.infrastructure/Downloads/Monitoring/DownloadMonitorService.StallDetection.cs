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
        internal const string StateChangedAtKey = "StateChangedAt";
        internal const string StallWarnedAtKey = "StallWarnedAt";

        /// <summary>
        /// Notices a download whose state has stopped moving.
        ///
        /// Every individual poll of a stuck download looks perfectly healthy — a status, a progress
        /// figure, no error — so nothing in the log ever says a transfer has gone nowhere for days.
        /// That silence is how a whole queue can sit at the same status indefinitely while the app
        /// reports no problem at all. Comparing each poll against the last state that actually
        /// differed turns "not moving" into something the log can say out loud.
        ///
        /// The bookkeeping lives in metadata rather than a column so this needs no migration, and
        /// the warning repeats on the same interval so a long stall stays visible without filling
        /// the log on every polling cycle.
        /// </summary>
        internal void TrackProgressStall(DownloadClientConfiguration client, Download current, Download? previous)
        {
            // Import-owned and finished states are not "stalled" in any sense the user can act on;
            // their progress is deliberately frozen while another worker owns them.
            if (current.Status is DownloadStatus.Completed
                or DownloadStatus.Moved
                or DownloadStatus.Failed
                or DownloadStatus.Processing
                or DownloadStatus.ImportPending
                or DownloadStatus.ImportBlocked
                or DownloadStatus.ImportNameMismatch)
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

            if (!TryReadTimestamp(current, StateChangedAtKey, out var changedAt))
            {
                // First poll after this code shipped, or a hand-edited row: start the clock now
                // rather than reporting a stall we have no evidence for.
                current.SetMetadata(StateChangedAtKey, now.ToString("O", CultureInfo.InvariantCulture));
                return;
            }

            var stalledFor = now - changedAt;
            if (stalledFor < StallWarningThreshold)
            {
                return;
            }

            if (TryReadTimestamp(current, StallWarnedAtKey, out var warnedAt) && now - warnedAt < StallWarningThreshold)
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

        private static bool TryReadTimestamp(Download download, string key, out DateTime value)
        {
            // Written with "O" against a UTC DateTime, so the stored text carries its own offset and
            // RoundtripKind is the correct style. It cannot be combined with AdjustToUniversal —
            // DateTime.TryParse throws on that pair — so the conversion happens after parsing.
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
