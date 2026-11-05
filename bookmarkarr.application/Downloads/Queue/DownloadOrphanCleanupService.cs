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
using Microsoft.Extensions.Logging;

namespace Bookmarkarr.Application.Downloads.Queue
{
    public sealed class DownloadOrphanCleanupService(
        IDownloadRepository downloadRepository,
        IAppMetricsService metrics,
        ILogger<DownloadOrphanCleanupService> logger)
    {
        private static readonly TimeSpan OrphanedDownloadGracePeriod = TimeSpan.FromMinutes(5);

        public async Task RemoveOrphansAsync(
            DownloadClientConfiguration client,
            ClientQueueFetchResult clientQueueResult,
            List<QueueItem> mappedQueueItems,
            List<Download> clientDownloads)
        {
            if (clientQueueResult.UsedCachedSnapshot)
            {
                logger.LogInformation(
                    "Skipping orphan cleanup for client {ClientName}: using cached queue snapshot",
                    client.Name);
                return;
            }

            if (clientQueueResult.IsUnavailable)
            {
                logger.LogWarning(
                    "Skipping orphan cleanup for client {ClientName}: no live queue snapshot was available",
                    client.Name);
                return;
            }

            var liveIds = BuildLiveClientItemIds(clientQueueResult.QueueItems, mappedQueueItems);
            var observedAt = clientQueueResult.SnapshotRefreshedAtUtc ?? DateTimeOffset.UtcNow;
            foreach (var download in clientDownloads)
            {
                var externalIds = DownloadQueueMetadataMatcher.GetKnownClientItemIds(download.Metadata)
                    .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var reason = GetCleanupReason(download, externalIds, liveIds, observedAt.UtcDateTime);
                if (reason is null)
                {
                    if (download.Metadata.Remove("QueueMissingSince"))
                    {
                        download.Metadata.Remove("QueueMissingSnapshot");
                        await downloadRepository.UpdateAsync(download);
                    }
                    continue;
                }

                // Persist the first observation so restarts cannot lose the grace period. A reused
                // snapshot never counts twice, and a client error never reaches this decision.
                var firstSeen = download.GetMetadataString("QueueMissingSince");
                if (!DateTimeOffset.TryParse(firstSeen, out var missingSince))
                {
                    download.SetMetadata("QueueMissingSince", observedAt.ToString("O"));
                    download.SetMetadata("QueueMissingSnapshot", observedAt.ToString("O"));
                    await downloadRepository.UpdateAsync(download);
                    logger.LogWarning("Download {DownloadId} is absent from a successful queue snapshot for {ClientName}; awaiting confirmation", download.Id, client.Name);
                    continue;
                }
                if (download.GetMetadataString("QueueMissingSnapshot") == observedAt.ToString("O")
                    || observedAt - missingSince < OrphanedDownloadGracePeriod)
                    continue;

                download.SetStatus(DownloadStatus.SourceMissing);
                download.ErrorMessage = "Download no longer exists in the client queue (confirmed by separate successful snapshots). Retry or choose another release.";
                await downloadRepository.UpdateAsync(download);
                logger.LogWarning("Marked download {DownloadId} SourceMissing after confirmed absence from {ClientName}; history retained", download.Id, client.Name);
                DownloadQueueDiagnostics.TryIncrementMetric(metrics, "download.orphan.source_missing");
            }
        }

        private static HashSet<string> BuildLiveClientItemIds(
            List<QueueItem> clientQueue,
            List<QueueItem> mappedQueueItems)
        {
            var liveClientItemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var mapped in mappedQueueItems.Where(item => !string.IsNullOrWhiteSpace(item.Id)))
            {
                liveClientItemIds.Add(mapped.Id!);
            }

            foreach (var itemId in clientQueue.Where(item => !string.IsNullOrWhiteSpace(item.Id)).Select(item => item.Id!))
            {
                liveClientItemIds.Add(itemId);
            }

            return liveClientItemIds;
        }

        private OrphanCleanupReason? GetCleanupReason(
            Download download,
            List<string> externalIds,
            HashSet<string> liveClientItemIds,
            DateTime now)
        {
            if (download.Status is not (DownloadStatus.Queued or DownloadStatus.Downloading or DownloadStatus.Paused))
            {
                return null;
            }

            // A full queue can be category-filtered while id-based monitoring spans both media.
            // Recent direct evidence that this item exists takes precedence over an empty category.
            if (DateTimeOffset.TryParse(download.GetMetadataString("ClientLastSeenAt"), out var lastSeen)
                && now - lastSeen.UtcDateTime < OrphanedDownloadGracePeriod)
                return null;

            var age = now - download.StartedAt;
            if (age < OrphanedDownloadGracePeriod)
            {
                logger.LogDebug(
                    "Skipping orphan cleanup for recent download {DownloadId} '{Title}' (age: {Age:F1} min)",
                    download.Id,
                    download.Title,
                    age.TotalMinutes);
                return null;
            }

            if (externalIds.Count == 0)
            {
                // Direct-download records are owned internally by Bookmarkarr. They do
                // not have a download-client queue item to reconcile against, so the
                // absence of ClientDownloadId/TorrentHash is valid only for DDL.
                if (string.Equals(download.DownloadClientId, DirectDownloadMetadataKeys.ClientId, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                return OrphanCleanupReason.MissingExternalId;
            }

            if (liveClientItemIds.Contains(download.Id) || externalIds.Any(liveClientItemIds.Contains))
            {
                return null;
            }

            return OrphanCleanupReason.MissingFromClientSnapshot;
        }

        private enum OrphanCleanupReason
        {
            MissingExternalId,
            MissingFromClientSnapshot
        }

    }
}
