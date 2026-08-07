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

namespace Bookmarkarr.Application.Downloads.Common
{
    /// <summary>
    /// Responsibilities:
    /// - Make sure any path reported by any download client adapter is mapped using adequate Remote Path Mapping
    /// - Single point of contact for any download client adapter, no download client adapter detail should be visible behind this
    /// - Persistence: Do not persist anything here, it's up to callers to know what they are doing
    /// </summary>
    public class DownloadClientGateway(
        IRemotePathMappingService remotePathMappingService,
        IDownloadClientAdapterFactory factory,
        IFileSystem fileSystem,
        ILogger<DownloadClientGateway> logger) : IDownloadClientGateway
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

        public Task<(bool Success, string Message)> TestConnectionAsync(DownloadClientConfiguration client, CancellationToken ct = default)
        {
            var adapter = ResolveAdapter(client);
            return adapter.TestConnectionAsync(client, ct);
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

            return await adapter.AddAsync(client, submission, ct);
        }

        public Task<bool> RemoveAsync(DownloadClientConfiguration client, string id, bool deleteFiles = false, CancellationToken ct = default)
        {
            // Removes the item from the external download client only. Database
            // removal belongs to the workflow that owns the durable state transition.
            var adapter = ResolveAdapter(client);
            return adapter.RemoveAsync(client, id, deleteFiles, ct);
        }

        public async Task<List<QueueItem>> GetQueueAsync(DownloadClientConfiguration client, CancellationToken ct = default)
        {
            var adapter = ResolveAdapter(client);
            var items = await adapter.GetQueueAsync(client, ct);
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
                items = await adapter.GetQueueAsync(client, ids!, ct);
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

                download = QueueItemConverter.UpdateFromQueueItem(download, item);
            }

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
        private async Task<QueueItem> TranslateQueueItemPathsAsync(DownloadClientConfiguration client, QueueItem item)
        {
            if (!string.IsNullOrWhiteSpace(item.RemotePath))
            {
                item.LocalPath = await remotePathMappingService.TranslatePathAsync(client, item.RemotePath);
            }

            if (!string.IsNullOrWhiteSpace(item.ContentPath))
            {
                item.ContentPath = await remotePathMappingService.TranslatePathAsync(client, item.ContentPath);
            }

            // Upstream context: https://github.com/Listenarrs/Listenarr/issues/592
            // Adapter ownership is still being clarified. Until that contract is tightened,
            // the gateway treats null and empty SourceFiles as "unknown" and derives a
            // file list only from a real ContentPath. Empty path strings are intentionally
            // ignored because some clients, especially active SABnzbd queue entries, do not
            // expose a completed storage path until history is available.
            if (item.SourceFiles != null && item.SourceFiles.Count > 0)
            {
                List<string> sourceFiles = [];
                foreach (string file in item.SourceFiles)
                {
                    var translatedPath = await remotePathMappingService.TranslatePathAsync(client, file);
                    sourceFiles.Add(ResolveExtensionShapedSingleFileDirectory(translatedPath) ?? translatedPath);
                }
                item.SourceFiles = sourceFiles;
            }
            else if (!string.IsNullOrWhiteSpace(item.ContentPath))
            {
                // Scan ContentPath only after the adapter has supplied a non-empty path.
                // Active queue snapshots may not be import-ready, so adapters should leave
                // ContentPath null until a reliable file or completed storage directory exists.
                if (fileSystem.FileExists(item.ContentPath))
                {
                    item.SourceFiles = [item.ContentPath];
                }
                else if (ResolveExtensionShapedSingleFileDirectory(item.ContentPath) is { } nestedMediaFile)
                {
                    item.SourceFiles = [nestedMediaFile];
                }
                else if (IsExtensionShapedMediaDirectory(item.ContentPath) && !IsUsenetClient(client))
                {
                    // An extension-shaped directory is a single-file client layout. If its
                    // exact nested file is unavailable or unsafe, do not widen discovery to
                    // arbitrary siblings or symlink targets.
                    LogMissingSourceFiles(
                        client,
                        item,
                        $"extension-shaped content directory {item.ContentPath} did not contain a safe exact nested media file",
                        null);
                    item.SourceFiles = [];
                }
                else
                {
                    // Some clients can only report a directory. Expand it so import code can
                    // operate on the specific files that belong to this download.
                    try
                    {
                        item.SourceFiles = [.. fileSystem
                            .EnumerateFiles(item.ContentPath, "*.*", SearchOption.AllDirectories)
                            .Select(f => FileUtils.NormalizeStoredPath(f))
                            .Where(f => !IsClientWorkingCopy(item.ContentPath, f))];
                    }
                    catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException or StackOverflowException))
                    {
                        LogMissingSourceFiles(
                            client,
                            item,
                            $"content path scanning failed with path {item.ContentPath}",
                            exception);
                        item.SourceFiles = [];
                    }
                }
            }
            else
            {
                if (IsImportSourceExpectedStatus(item.Status))
                {
                    LogMissingSourceFiles(client, item, "no content path", null);
                }

                item.SourceFiles = [];
            }

            // Remove duplicates if any
            item.SourceFiles = new HashSet<string>(item.SourceFiles, StringComparer.OrdinalIgnoreCase).ToList();

            return item;
        }

        /// <summary>
        /// qBittorrent-compatible clients can represent a single-file torrent as a directory
        /// whose name (including its media extension) is repeated by the only intended file.
        /// Resolve that exact nested file without recursively selecting unrelated siblings.
        /// </summary>
        private string? ResolveExtensionShapedSingleFileDirectory(string path)
        {
            if (!IsExtensionShapedMediaDirectory(path))
            {
                return null;
            }

            var normalizedDirectory = FileUtils.NormalizeStoredPath(path).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            var directoryName = Path.GetFileName(normalizedDirectory);

            try
            {
                var directoryFullPath = Path.GetFullPath(normalizedDirectory);
                var candidateFullPath = Path.GetFullPath(Path.Combine(directoryFullPath, directoryName));
                var relativePath = Path.GetRelativePath(directoryFullPath, candidateFullPath);
                if (relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || string.Equals(relativePath, "..", StringComparison.Ordinal)
                    || Path.IsPathRooted(relativePath))
                {
                    logger.LogWarning(
                        "Rejected nested single-file download path outside resolved directory {DownloadDirectory}",
                        normalizedDirectory);
                    return null;
                }

                // Do not follow a directory or file symlink outside the mapped download tree.
                if (fileSystem.IsReparsePoint(directoryFullPath)
                    || (fileSystem.FileExists(candidateFullPath) && fileSystem.IsReparsePoint(candidateFullPath)))
                {
                    logger.LogWarning(
                        "Rejected reparse-point nested single-file download path under {DownloadDirectory}",
                        normalizedDirectory);
                    return null;
                }

                if (!fileSystem.FileExists(candidateFullPath))
                {
                    return null;
                }

                logger.LogInformation(
                    "Resolved extension-shaped download directory {DownloadDirectory} to exact nested media file {MediaFile}",
                    normalizedDirectory,
                    candidateFullPath);
                return FileUtils.NormalizeStoredPath(candidateFullPath);
            }
            catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException or StackOverflowException))
            {
                logger.LogWarning(
                    exception,
                    "Could not safely resolve extension-shaped download directory {DownloadDirectory}",
                    normalizedDirectory);
                return null;
            }
        }

        /// <summary>
        /// Usenet clients name the completed directory after the release. A release whose name
        /// ends in a media extension therefore produces a directory that looks identical to the
        /// qBittorrent single-file layout, but its payload is nested under the unpacked release
        /// folder rather than repeated as a same-named file. Refusing to recurse for those
        /// clients strands a perfectly good download in Import Blocked.
        ///
        /// The adapter's declared protocol is the authority here rather than a hardcoded list of
        /// client type names, so a newly added usenet client is covered without touching this.
        /// </summary>
        private bool IsUsenetClient(DownloadClientConfiguration client)
        {
            try
            {
                return ResolveAdapter(client).Protocol == DownloadProtocol.Usenet;
            }
            catch (InvalidOperationException)
            {
                // No adapter: keep the conservative single-file behaviour rather than widening
                // discovery for a client we cannot identify.
                return false;
            }
        }

        /// <summary>
        /// True for files under a client's in-progress working directory.
        ///
        /// NZBGet unpacks into <c>_unpack</c> beneath the destination and leaves that copy behind,
        /// so a completed download can expose the same media twice. Importing both stores the book
        /// twice and doubles the space it occupies.
        /// </summary>
        private static bool IsClientWorkingCopy(string contentPath, string filePath)
        {
            var relative = Path.GetRelativePath(
                FileUtils.NormalizeStoredPath(contentPath),
                FileUtils.NormalizeStoredPath(filePath));

            return relative
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => ClientWorkingDirectoryNames.Contains(segment));
        }

        private static readonly HashSet<string> ClientWorkingDirectoryNames =
            new(StringComparer.OrdinalIgnoreCase) { "_unpack", "_failed" };

        private bool IsExtensionShapedMediaDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !fileSystem.DirectoryExists(path))
            {
                return false;
            }

            var directoryName = Path.GetFileName(path.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar));
            return !string.IsNullOrWhiteSpace(directoryName)
                && (FileUtils.IsAudioFile(directoryName) || FileUtils.IsEbookFile(directoryName));
        }

        private void LogMissingSourceFiles(
            DownloadClientConfiguration client,
            QueueItem item,
            string reason,
            Exception? exception)
        {
            if (!IsImportSourceExpectedStatus(item.Status))
            {
                return;
            }

            logger.LogWarning(
                exception,
                "Download client {ClientId} reported no source files and {Reason} for item {Title}",
                client.Id,
                reason,
                item.Title);
        }

        internal static bool IsImportSourceExpectedStatus(string? status)
        {
            return string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "success", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "processing", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "importpending", StringComparison.OrdinalIgnoreCase);
        }
    }
}
