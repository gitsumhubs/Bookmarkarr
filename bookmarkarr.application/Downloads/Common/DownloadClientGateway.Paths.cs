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
    public partial class DownloadClientGateway
    {
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
