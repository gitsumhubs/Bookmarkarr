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
using Bookmarkarr.Domain.Audiobooks.Enumerations;
using Bookmarkarr.Domain.Downloads;
using Bookmarkarr.Infrastructure.Downloads.Processing;
using Microsoft.AspNetCore.Mvc;

namespace Bookmarkarr.Api.Features.Downloads
{
    /// <summary>Reports and repairs filenames a download client mangled as it wrote them.</summary>
    /// <remarks>
    /// The repair is a rename on disk to the name the client reports, which is the name the import
    /// is looking for. Renaming rather than re-pointing the import keeps one source of truth: the
    /// client's file list stays authoritative, and a later re-poll or reprocess still lines up.
    /// </remarks>
    public sealed class ImportNameFixWorkflow(
        IDownloadRepository downloadRepository,
        IDownloadService downloadService,
        IFileSystem fileSystem,
        IFileMover fileMover,
        ILogger<ImportNameFixWorkflow> logger)
    {
        public async Task<IActionResult> GetIssuesAsync(string downloadId)
        {
            var download = await downloadRepository.FindAsync(downloadId);
            if (download == null)
            {
                return new NotFoundObjectResult(new { message = $"Download {downloadId} not found" });
            }

            var mismatches = Read(download);
            return new OkObjectResult(new
            {
                downloadId,
                status = download.Status.ToString(),
                count = mismatches.Count,
                issues = mismatches.Select(m => new
                {
                    reportedName = Path.GetFileName(m.ReportedPath),
                    actualName = Path.GetFileName(m.ActualPath),
                    directory = Path.GetDirectoryName(m.ReportedPath),
                    reportedPath = m.ReportedPath,
                    actualPath = m.ActualPath,
                    actualExists = fileSystem.FileExists(m.ActualPath),
                    reportedExists = fileSystem.FileExists(m.ReportedPath)
                })
            });
        }

        /// <param name="overrides">
        /// Optional operator corrections, keyed by reported path, naming the file to use instead of
        /// the detected one. Anything omitted keeps the detected pairing.
        /// </param>
        public async Task<IActionResult> ApplyAsync(string downloadId, IDictionary<string, string>? overrides)
        {
            var download = await downloadRepository.FindAsync(downloadId);
            if (download == null)
            {
                return new NotFoundObjectResult(new { message = $"Download {downloadId} not found" });
            }

            var mismatches = Read(download);
            if (mismatches.Count == 0)
            {
                return new BadRequestObjectResult(new { message = "This download has no detected filename issues" });
            }

            var renamed = new List<object>();
            var failures = new List<object>();

            foreach (var mismatch in mismatches)
            {
                var source = overrides is not null && overrides.TryGetValue(mismatch.ReportedPath, out var chosen)
                    && !string.IsNullOrWhiteSpace(chosen)
                        ? chosen
                        : mismatch.ActualPath;

                try
                {
                    // Both sides must stay inside the directory the client reported, so a supplied
                    // override cannot be used to move an arbitrary file into the library.
                    var expectedDirectory = Path.GetFullPath(Path.GetDirectoryName(mismatch.ReportedPath) ?? string.Empty);
                    var sourceFull = Path.GetFullPath(source);
                    if (!string.Equals(Path.GetDirectoryName(sourceFull), expectedDirectory, StringComparison.OrdinalIgnoreCase))
                    {
                        failures.Add(new { mismatch.ReportedPath, error = "Chosen file is outside the download's own directory" });
                        continue;
                    }

                    if (fileSystem.FileExists(mismatch.ReportedPath))
                    {
                        // Already correct — treat as done rather than failing the whole repair.
                        renamed.Add(new { mismatch.ReportedPath, note = "Already present under the reported name" });
                        continue;
                    }

                    if (!fileSystem.FileExists(sourceFull))
                    {
                        failures.Add(new { mismatch.ReportedPath, error = $"File not found: {sourceFull}" });
                        continue;
                    }

                    // Through the mover rather than System.IO: filesystem access belongs to the
                    // layer that owns it, and the API layer is checked for exactly this.
                    if (!await fileMover.PerformActionOn(FileAction.Move, sourceFull, mismatch.ReportedPath))
                    {
                        failures.Add(new { mismatch.ReportedPath, error = "Rename failed" });
                        continue;
                    }

                    renamed.Add(new { from = sourceFull, to = mismatch.ReportedPath });
                    logger.LogInformation(
                        "Renamed {From} to {To} to repair import for download {DownloadId}",
                        sourceFull,
                        mismatch.ReportedPath,
                        downloadId);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failures.Add(new { mismatch.ReportedPath, error = ex.Message });
                    logger.LogWarning(ex, "Failed to rename a file repairing import for download {DownloadId}", downloadId);
                }
            }

            if (renamed.Count == 0)
            {
                return new ObjectResult(new { message = "No files could be renamed", failures })
                {
                    StatusCode = StatusCodes.Status422UnprocessableEntity
                };
            }

            // Cleared before requeueing so a second failure re-detects from scratch rather than
            // replaying stale pairs against files that have since moved.
            download.Metadata?.Remove(DownloadProcessingJobProcessor.NameMismatchMetadataKey);
            download.SetStatus(DownloadStatus.ImportPending);
            await downloadService.UpdateAsync(download);

            // Reuses the same path the Reprocess action uses, so a repaired import goes through
            // exactly the pipeline a normal one does rather than a parallel route of its own.
            await downloadService.ReprocessDownloadAsync(downloadId);

            return new OkObjectResult(new
            {
                message = $"Renamed {renamed.Count} file(s); import requeued",
                renamed,
                failures
            });
        }

        private static List<ImportNameMismatch> Read(Download download)
        {
            var raw = download.GetMetadataString(DownloadProcessingJobProcessor.NameMismatchMetadataKey);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return [];
            }

            try
            {
                return JsonSerializer.Deserialize<List<ImportNameMismatch>>(raw) ?? [];
            }
            catch (JsonException)
            {
                return [];
            }
        }
    }
}
