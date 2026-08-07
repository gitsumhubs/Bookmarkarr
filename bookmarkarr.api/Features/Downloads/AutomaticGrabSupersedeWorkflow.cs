/*
 * Bookmarkarr - Audiobook Management System
 * Copyright (C) 2024-2026 Bookmarkarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Bookmarkarr.Application.Downloads.Submission;
using Bookmarkarr.Domain.Downloads;

namespace Bookmarkarr.Api.Features.Downloads
{
    /// <summary>
    /// Cancels an automatic grab so a manually chosen release can take its place.
    /// </summary>
    /// <remarks>
    /// A person picking a specific release knows more than scoring does. Without this, the
    /// duplicate guard rejected the manual grab with a 409 and the automatic one kept running:
    /// on a live install automatic search grabbed book 3 of a series for a book that is book 1,
    /// the operator grabbed the right release six seconds later, and it was silently discarded.
    ///
    /// Only automatic downloads are superseded. A manual grab never displaces another manual grab,
    /// because that is a person changing their mind about two deliberate choices and should be
    /// their call, not a silent replacement.
    /// </remarks>
    public sealed class AutomaticGrabSupersedeWorkflow(
        IDownloadRepository downloadRepository,
        IDownloadService downloadService,
        ILogger<AutomaticGrabSupersedeWorkflow> logger)
    {
        private static readonly DownloadStatus[] InFlight =
        [
            DownloadStatus.Queued,
            DownloadStatus.Downloading,
            DownloadStatus.Paused,
            DownloadStatus.ImportPending
        ];

        /// <summary>Returns the ids of the automatic downloads that were cancelled.</summary>
        public async Task<IReadOnlyList<string>> SupersedeAsync(int audiobookId, bool isEbook)
        {
            var superseded = new List<string>();

            var candidates = (await downloadRepository.GetAllAsync())
                .Where(d => d.AudiobookId == audiobookId
                    && DownloadContentTypes.IsEbook(d) == isEbook
                    && InFlight.Contains(d.Status)
                    && !d.IsManualGrab())
                .ToList();

            foreach (var candidate in candidates)
            {
                try
                {
                    // Not blocklisted: the release did nothing wrong, it simply lost to a human's
                    // choice, and blocklisting would bar it from a legitimate future grab.
                    await downloadService.RemoveFromQueueAsync(
                        candidate.Id,
                        candidate.DownloadClientId,
                        force: true,
                        blocklist: false);

                    superseded.Add(candidate.Id);
                    logger.LogInformation(
                        "Superseded automatic download {DownloadId} for audiobook {AudiobookId} in favour of a manual grab",
                        candidate.Id,
                        audiobookId);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Reported rather than thrown: failing to cancel the old grab must not stop the
                    // new one. The worst case is the duplicate guard rejecting the manual grab,
                    // which is exactly the behaviour that existed before.
                    logger.LogWarning(
                        ex,
                        "Could not supersede automatic download {DownloadId} for audiobook {AudiobookId}",
                        candidate.Id,
                        audiobookId);
                }
            }

            return superseded;
        }
    }
}
