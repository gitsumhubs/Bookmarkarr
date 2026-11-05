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
using Bookmarkarr.Application.Common;
using Bookmarkarr.Application.Mapping;
using Bookmarkarr.Domain.Common;
using Bookmarkarr.Domain.Downloads.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;

namespace Bookmarkarr.Application.Downloads.Common
{
    /// <summary>
    /// Responsibilities:
    /// - Make sure any path reported by any download client adapter is mapped using adequate Remote Path Mapping
    /// - Single point of contact for any download client adapter, no download client adapter detail should be visible behind this
    /// - Persistence: Do not persist anything here, it's up to callers to know what they are doing
    /// </summary>
    public partial class DownloadClientGateway(
        IRemotePathMappingService remotePathMappingService,
        IDownloadClientAdapterFactory factory,
        IFileSystem fileSystem,
        ILogger<DownloadClientGateway> logger,
        ClientQueueRequestCache? queueCache = null,
        IMemoryCache? diagnosticsCache = null) : IDownloadClientGateway
    {
        internal IDownloadClientAdapter ResolveAdapter(DownloadClientConfiguration client)
        {
            ArgumentNullException.ThrowIfNull(client);

            if (!string.IsNullOrWhiteSpace(client.Type))
            {
                try
                {
                    return factory.GetByType(client.Type);
                }
                catch (InvalidOperationException)
                {
                }
            }

            var descriptor = !string.IsNullOrWhiteSpace(client.Name)
                ? $"{client.Name} ({client.Type ?? "unknown"})"
                : client.Type ?? client.Id ?? "unknown";

            var message = $"No download client adapter registered for {LogRedaction.SanitizeText(descriptor)}.";
            logger.LogError(message);
            throw new InvalidOperationException(message);
        }

        public async Task<(bool Success, string Message)> TestConnectionAsync(DownloadClientConfiguration client, CancellationToken ct = default)
        {
            var adapter = ResolveAdapter(client);
            var result = await adapter.TestConnectionAsync(client, ct);
            if (!result.Success)
            {
                return result;
            }

            // Reaching the API is only half of what "working" means. A client that reports host
            // paths rather than its own container paths answers every request perfectly and still
            // hands over directories this process cannot open, so a connection-only test passes
            // while every import from it is guaranteed to fail. Checking the paths here is what
            // turns that into an answer at the point the user is already asking the question.
            //
            // The result stays successful on purpose: the connection genuinely is fine, and the
            // two concerns should not be collapsed into one boolean. The warning rides along in
            // the message, which is where the user is already reading.
            var pathIssue = await DescribeUnreachableClientPathsAsync(client, ct);
            return pathIssue == null ? result : (result.Success, $"{result.Message} {pathIssue}");
        }
        public async Task<DownloadClientSubmissionResult> AddAsync(
            DownloadClientConfiguration client,
            PreparedDownloadSubmission submission,
            CancellationToken ct = default)
        {
            var adapter = ResolveAdapter(client);
            if (!adapter.Protocols.Contains(submission.Protocol))
            {
                throw new DownloadClientSubmissionException(
                    $"Download client {client.Name ?? client.Type} does not support the prepared {submission.Protocol} submission.");
            }

            var result = await adapter.AddAsync(client, submission, ct);
            queueCache?.Invalidate();
            return result;
        }

        public async Task<bool> RemoveAsync(DownloadClientConfiguration client, string id, bool deleteFiles = false, CancellationToken ct = default)
        {
            // Removes the item from the external download client only. Database
            // removal belongs to the workflow that owns the durable state transition.
            var adapter = ResolveAdapter(client);
            var removed = await adapter.RemoveAsync(client, id, deleteFiles, ct);
            if (removed) queueCache?.Invalidate();
            return removed;
        }

        public async Task<List<QueueItem>> GetQueueAsync(DownloadClientConfiguration client, CancellationToken ct = default)
        {
            var adapter = ResolveAdapter(client);
            var items = queueCache is null ? await adapter.GetQueueAsync(client, ct)
                : await queueCache.GetAsync(client, null, () => adapter.GetQueueAsync(client, ct), ct);
            var tasks = items.Select(item => TranslateQueueItemPathsAsync(client, item));
            return [.. await Task.WhenAll(tasks)];
        }

        public async Task<bool> MarkItemAsImportedAsync(DownloadClientConfiguration client, Download download, CancellationToken ct = default)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            if (download == null)
            {
                throw new ArgumentNullException(nameof(download));
            }

            var externalId = download.GetExternalId();
            if (string.IsNullOrEmpty(externalId))
            {
                return true;
            }

            if (!client.IsEnabled)
            {
                logger.LogDebug(
                    "Skipping mark imported for download {DownloadId}: download client {ClientId} is disabled",
                    download.Id,
                    client.Id);
                return true;
            }

            var adapter = ResolveAdapter(client);
            return await adapter.MarkItemAsImportedAsync(client, externalId, ct);
        }

        public async Task<QueueItem> GetQueueItemAsync(
            DownloadClientConfiguration client,
            Download download,
            QueueItem queueItem,
            CancellationToken ct = default)
        {
            var adapter = ResolveAdapter(client);
            var item = await adapter.GetImportItemAsync(client, download, queueItem, null, ct);

            return await TranslateQueueItemPathsAsync(client, item);
        }

        public async Task<List<Download>> FetchDownloadsAsync(DownloadClientConfiguration client, List<Download> downloads, CancellationToken ct = default)
        {
            var ids = GetExternalIds(downloads);
            if (ids.Count == 0)
            {
                return downloads;
            }

            var adapter = ResolveAdapter(client);
            List<QueueItem> items;
            try
            {
                items = queueCache is null ? await adapter.GetQueueAsync(client, ids!, ct)
                    : await queueCache.GetAsync(client, ids, () => adapter.GetQueueAsync(client, ids!, ct), ct);
            }
            catch (DownloadClientAdapterPollingException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                throw new DownloadClientAdapterPollingException(
                    $"Error polling download client {client.Name ?? client.Id ?? client.Type}.",
                    ex);
            }

            var tasks = items.Select(item => TranslateQueueItemPathsAsync(client, item));
            items = [.. await Task.WhenAll(tasks)];

            var matchedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (QueueItem item in items)
            {
                var download = downloads.FirstOrDefault(d =>
                    string.Equals(d.GetExternalId(), item.Id, StringComparison.OrdinalIgnoreCase));
                if (download == null)
                {
                    continue;
                }

                logger.LogDebug(
                    "Found matching download client item for {DownloadId}: {Title} (ExternalId: {ExternalId}, Status: {Status}, Progress: {Progress:F2}, LocalPath: {LocalPath}, ContentPath: {ContentPath})",
                    download.Id,
                    item.Title,
                    item.Id,
                    item.Status,
                    item.Progress,
                    item.LocalPath,
                    item.ContentPath);

                var normalizedState = (item.Status ?? string.Empty).ToLowerInvariant();
                var isExplicitCompletedState = normalizedState is "completed" or "success";

                // Mirrors QueueItemConverter: zero downloaded bytes alongside a completion claim
                // means the client does not report byte counts, not that nothing has transferred.
                // Kept in step so this diagnostic never contradicts the decision actually taken.
                var reportsNoByteProgress = item.Downloaded <= 0
                    && (isExplicitCompletedState || (double.IsFinite(item.Progress) && item.Progress >= 100));
                var hasReliableSize = item.Size > 0 && item.Downloaded >= 0 && !reportsNoByteProgress;
                var amountLeft = hasReliableSize
                    ? Math.Max(0, item.Size - item.Downloaded)
                    : null as long?;

                logger.LogDebug(
                    "Completion diagnostic for {DownloadId}: Progress={Progress:F4}, HasReliableSize={HasReliableSize}, AmountLeft={AmountLeft}, ExplicitCompletedState={ExplicitCompletedState}, Status={Status}",
                    download.Id,
                    item.Progress,
                    hasReliableSize,
                    amountLeft,
                    isExplicitCompletedState,
                    item.Status);

                matchedIds.Add(item.Id);
                download = QueueItemConverter.UpdateFromQueueItem(download, item);
            }

            WarnOnUnreportedDownloads(client, ids!, matchedIds, items.Count);

            return downloads;
        }

        /// <summary>
        /// Give the list of external IDs from a list of download
        /// </summary>
        /// <param name="downloads"></param>
        /// <returns></returns>
        private List<string> GetExternalIds(List<Download> downloads)
        {
            return downloads
                .Select(d => d.GetExternalId())
                .Where(id => id != null)
                .ToHashSet()
                .ToList()!;
        }

        /// <summary>
        /// Handles path mapping of queue item
        /// Make sure all paths are locally accessible after processing and
        /// that a proper list of sanitized source files is produced
        /// </summary>
        /// <param name="client">Download client configuration to use for path mapping</param>
        /// <param name="item">Queue item to translate/sanitize</param>
        /// <returns></returns>
    }
}
