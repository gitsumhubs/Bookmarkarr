/*
 * Bookmarkarr - Audiobook Management System
 * Copyright (C) 2024-2026 Bookmarkarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using System.Text.Json;
using Bookmarkarr.Application.Downloads.Contracts;
using Bookmarkarr.Application.Downloads.Import;
using Bookmarkarr.Domain.Downloads;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bookmarkarr.Infrastructure.Downloads.Processing
{
    public partial class DownloadProcessingJobProcessor
    {
        /// <summary>Metadata key holding the detected reported/actual filename pairs.</summary>
        public const string NameMismatchMetadataKey = "ImportNameMismatches";

        /// <summary>
        /// Parks the download for operator repair when the client renamed files as it wrote them.
        /// </summary>
        /// <remarks>
        /// Returns false when nothing conclusive is found, leaving the caller to retry exactly as
        /// before — a genuinely still-transferring file must keep its retries, and only an
        /// unambiguous near-match in the same directory is treated as a rename.
        /// </remarks>
        private async Task<bool> TryFlagNameMismatchAsync(
            IServiceScope scope,
            DownloadProcessingJob job,
            Download download,
            IReadOnlyList<string> missingPrimaryFiles,
            IReadOnlyList<string> filesPresent,
            QueueItem queueItem,
            IDownloadProcessingJobService jobService,
            CancellationToken cancellationToken)
        {
            List<string> onDisk;
            try
            {
                // Look only where the missing files were expected; a wider sweep would invite
                // matches from unrelated releases.
                onDisk = missingPrimaryFiles
                    .Select(Path.GetDirectoryName)
                    .Where(directory => !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .SelectMany(directory => Directory.EnumerateFiles(directory!))
                    .ToList();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not inspect directories while checking download {DownloadId} for renamed files", download.Id);
                return false;
            }

            var mismatches = ImportNameMismatchDetector.Detect(
                missingPrimaryFiles,
                queueItem.SourceFiles?.Where(File.Exists).ToList() ?? [.. filesPresent],
                onDisk);

            if (mismatches.Count == 0)
            {
                return false;
            }

            download.SetMetadata(NameMismatchMetadataKey, JsonSerializer.Serialize(mismatches));
            download.SetStatus(DownloadStatus.ImportNameMismatch);

            var downloadService = scope.ServiceProvider.GetRequiredService<IDownloadService>();
            await downloadService.UpdateAsync(download);

            foreach (var mismatch in mismatches)
            {
                job.AddLogEntry(
                    $"Client reported '{Path.GetFileName(mismatch.ReportedPath)}' but wrote '{Path.GetFileName(mismatch.ActualPath)}'");
            }

            job.AddLogEntry($"Import needs a name fix for {mismatches.Count} file(s); waiting for confirmation");
            await jobService.UpdateJobAsync(job.MarkAsCompleted());

            logger.LogInformation(
                "Download {DownloadId} parked for name repair: {Count} file(s) exist under names the client did not report",
                download.Id,
                mismatches.Count);

            return true;
        }
    }
}
