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

using Bookmarkarr.Application.Audiobooks.Matching;
using Bookmarkarr.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Bookmarkarr.Infrastructure.HostedServices.Search
{
    internal sealed class AutomaticSearchQualityEvaluator(ILogger logger)
    {
        /// <param name="diskPresence">
        /// What the book's destination directory actually holds. Passed in rather than looked up
        /// here so the database-only answer stays testable on its own.
        /// </param>
        public async Task<(bool cutoffMet, string? bestExistingQuality)> GetExistingQualityAsync(
            Audiobook audiobook,
            IDownloadRepository downloadRepository,
            IAudiobookFileRepository fileRepository,
            LibraryPresence diskPresence = default,
            CancellationToken ct = default)
        {
            // Cutoff is evaluated by the shared QualityMatcher-backed evaluator so automatic search,
            // library status, and the API all agree on what "cutoff met" means.
            var cutoffMet = await AudiobookQualityCutoffEvaluator.IsQualityCutoffMetAsync(
                audiobook, downloadRepository, fileRepository, logger);
            string? bestQuality = null;

            var allDownloads = await downloadRepository.GetByAudiobookIdAsync(audiobook.Id, ct);
            var existingDownloads = allDownloads.Where(d => d.Status == DownloadStatus.Completed).ToList();

            foreach (var dl in existingDownloads.Where(dl => dl.Metadata != null))
            {
                if (dl.Metadata!.TryGetValue("Quality", out var qobj) && qobj != null)
                {
                    var q = qobj.ToString();
                    if (!string.IsNullOrEmpty(q))
                    {
                        if (bestQuality == null) bestQuality = q;
                        else if (IsQualityBetter(q, bestQuality, audiobook.QualityProfile)) bestQuality = q;
                    }
                }
            }

            var existingFiles = await fileRepository.GetByAudiobookIdAsync(audiobook.Id, ct);

            foreach (var fq in existingFiles
                .Select(f => AudiobookQualityCutoffEvaluator.ResolveFileQualityLabel(f, audiobook.QualityProfile))
                .Where(fq => !string.IsNullOrEmpty(fq)))
            {
                if (bestQuality == null) bestQuality = fq;
                else if (IsQualityBetter(fq, bestQuality, audiobook.QualityProfile)) bestQuality = fq;
            }

            // Files on disk that no row knows about still count as "already have it". Their quality
            // is derived from extension alone and therefore rounds down, so folding it in here can
            // only ever make an upgrade look more attractive, never less.
            if (!string.IsNullOrEmpty(diskPresence.BestQuality)
                && (bestQuality == null || IsQualityBetter(diskPresence.BestQuality, bestQuality, audiobook.QualityProfile)))
            {
                bestQuality = diskPresence.BestQuality;
            }

            return (cutoffMet, bestQuality);
        }

        public bool IsQualityBetter(string? candidateQuality, string? existingQuality, QualityProfile? profile)
            => QualityMatcher.IsLabelBetter(candidateQuality, existingQuality, profile);
    }
}
