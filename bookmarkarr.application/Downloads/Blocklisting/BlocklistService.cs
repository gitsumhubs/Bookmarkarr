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

namespace Bookmarkarr.Application.Downloads.Blocklisting
{
    /// <summary>
    /// Counts repeated download failures of the same release and blocklists it once the
    /// threshold is reached.
    ///
    /// Only genuine *download* failures count — a stalled torrent, a client error, a failed
    /// unpack. Import blocks are deliberately excluded: those mean the bytes arrived and
    /// Bookmarkarr could not file them, so blocklisting on an import block would discard a
    /// perfectly good release because of a bug on our side.
    /// </summary>
    public class BlocklistService(
        IBlocklistRepository blocklistRepository,
        IDownloadRepository downloadRepository,
        ILogger<BlocklistService> logger) : IBlocklistService
    {
        public async Task<BlocklistEntry?> RecordFailureAsync(Download download, CancellationToken ct = default)
        {
            if (download.AudiobookId is not int audiobookId || audiobookId <= 0)
            {
                // Without a book there is nothing to scope the entry to, and a global block is
                // never wanted.
                return null;
            }

            var normalizedTitle = ReleaseIdentity.NormalizeReleaseName(download.Title);

            var existing = (await blocklistRepository.GetByAudiobookIdAsync(audiobookId, ct))
                .FirstOrDefault(entry => entry.Matches(
                    download.ReleaseGuid, download.IndexerId, download.Title, download.TotalSize));

            if (existing != null)
            {
                logger.LogDebug(
                    "Release '{Title}' is already blocklisted for audiobook {AudiobookId}",
                    LogRedaction.SanitizeText(download.Title),
                    audiobookId);
                return existing;
            }

            var failureCount = await CountFailuresAsync(audiobookId, download, ct);

            if (failureCount < BlocklistEntry.FailureThreshold)
            {
                logger.LogInformation(
                    "Release '{Title}' has failed {FailureCount} of {Threshold} times for audiobook {AudiobookId}; not blocklisting yet",
                    LogRedaction.SanitizeText(download.Title),
                    failureCount,
                    BlocklistEntry.FailureThreshold,
                    audiobookId);
                return null;
            }

            var entry = await AddEntryAsync(download, audiobookId, normalizedTitle, failureCount, download.ErrorMessage, ct);

            logger.LogWarning(
                "Blocklisted release '{Title}' for audiobook {AudiobookId} after {FailureCount} failed downloads: {Reason}",
                LogRedaction.SanitizeText(download.Title),
                audiobookId,
                failureCount,
                LogRedaction.SanitizeText(download.ErrorMessage ?? "no reason reported"));

            return entry;
        }

        public async Task<BlocklistEntry?> BlocklistReleaseAsync(Download download, string reason, CancellationToken ct = default)
        {
            if (download.AudiobookId is not int audiobookId || audiobookId <= 0)
            {
                return null;
            }

            var existing = (await blocklistRepository.GetByAudiobookIdAsync(audiobookId, ct))
                .FirstOrDefault(entry => entry.Matches(
                    download.ReleaseGuid, download.IndexerId, download.Title, download.TotalSize));

            if (existing != null)
            {
                return existing;
            }

            // Counted rather than assumed to be 1: the release may already have failed on its own
            // before the external caller stepped in, and the entry should reflect the real tally.
            var failureCount = Math.Max(1, await CountFailuresAsync(audiobookId, download, ct));

            var entry = await AddEntryAsync(
                download, audiobookId, ReleaseIdentity.NormalizeReleaseName(download.Title), failureCount, reason, ct);

            logger.LogWarning(
                "Blocklisted release '{Title}' for audiobook {AudiobookId} on external request: {Reason}",
                LogRedaction.SanitizeText(download.Title),
                audiobookId,
                LogRedaction.SanitizeText(reason));

            return entry;
        }

        private Task<BlocklistEntry> AddEntryAsync(
            Download download,
            int audiobookId,
            string normalizedTitle,
            int failureCount,
            string? reason,
            CancellationToken ct) =>
            blocklistRepository.AddAsync(new BlocklistEntry
            {
                AudiobookId = audiobookId,
                EditionId = download.EditionId,
                ReleaseGuid = download.ReleaseGuid,
                IndexerId = download.IndexerId,
                Title = download.Title,
                NormalizedTitle = normalizedTitle,
                Size = download.TotalSize,
                Source = download.GetMetadataString("Source"),
                Protocol = download.GetMetadataString("DownloadType"),
                FailureCount = failureCount,
                Reason = reason
            }, ct);

        public async Task<IReadOnlyList<BlocklistEntry>> GetForAudiobookAsync(int audiobookId, CancellationToken ct = default) =>
            await blocklistRepository.GetByAudiobookIdAsync(audiobookId, ct);

        /// <summary>
        /// How many times this exact release has failed for this book, counted from the download
        /// rows themselves so the tally survives a restart and needs no separate counter to keep
        /// in sync.
        /// </summary>
        private async Task<int> CountFailuresAsync(int audiobookId, Download download, CancellationToken ct)
        {
            var downloads = await downloadRepository.GetByAudiobookIdAsync(audiobookId, ct);

            return downloads.Count(candidate =>
                candidate.Status == DownloadStatus.Failed
                && ReleaseIdentity.Matches(
                    download.ReleaseGuid,
                    download.IndexerId,
                    ReleaseIdentity.NormalizeReleaseName(download.Title),
                    download.TotalSize,
                    candidate.ReleaseGuid,
                    candidate.IndexerId,
                    candidate.Title,
                    candidate.TotalSize));
        }
    }
}
