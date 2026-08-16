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
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Bookmarkarr.Domain.Common;
using Bookmarkarr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Bookmarkarr.Infrastructure.Downloads.Processing
{
    /// <summary>
    /// Process the download processing jobs queued
    /// </summary>
    public partial class DownloadProcessingJobProcessor(
        IServiceScopeFactory scopeFactory,
        ILogger<DownloadProcessingJobProcessor> logger,
        IAppMetricsService metrics,
        IScanQueueService scanQueueService) : BackgroundService, IDownloadImportProcessor
    {
        private readonly TimeSpan _processingInterval = TimeSpan.FromSeconds(10); // Check every 10 seconds

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("Download Processing Background Service started");

            // On startup, reset any jobs stuck in Processing status (from previous crash/restart)
            try
            {
                using var scope = scopeFactory.CreateScope();
                var downloadProcessingJobService = scope.ServiceProvider.GetRequiredService<IDownloadProcessingJobService>();
                await downloadProcessingJobService.ResetStuckJobsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation("Download processing startup reset canceled during shutdown");
            }
            catch (OperationCanceledException ex)
            {
                logger.LogWarning(ex, "Download processing startup reset canceled/timed out; continuing");
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                logger.LogError(ex, "Failed to reset stuck jobs on startup");
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessQueueAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (OperationCanceledException ex)
                {
                    logger.LogWarning(ex, "Download processing cycle canceled/timed out; continuing");
                }
                catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
                {
                    logger.LogError(ex, "Error processing download queue");
                }

                try
                {
                    await Task.Delay(_processingInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            logger.LogInformation("Download Processing Background Service stopped");
        }

        public async Task ProcessQueueAsync(CancellationToken cancellationToken)
        {
            using var scope = scopeFactory.CreateScope();
            var downloadProcessingJobService = scope.ServiceProvider.GetRequiredService<IDownloadProcessingJobService>();
            var downloadRepository = scope.ServiceProvider.GetRequiredService<IDownloadRepository>();
            var downloadService = scope.ServiceProvider.GetRequiredService<IDownloadService>();

            var job = await downloadProcessingJobService.GetNextJobAsync();
            if (job == null)
            {
                return;
            }

            using var logScope = logger.BeginScope(new Dictionary<string, object?>
            {
                ["JobId"] = job.Id,
                ["DownloadId"] = job.DownloadId,
                ["CorrelationId"] = job.GetOrCreateCorrelationId()
            });
            try
            {
                await ProcessJobAsync(job, cancellationToken);
            }
            catch (DownloadProcessingException exception)
            {
                logger.LogError($"Job {job.Id} failed: {exception.Message}");
                await downloadProcessingJobService.UpdateJobAsync(job.MarkAsFailed(exception.Message));
            }

            if (job.Status == ProcessingJobStatus.Failed)
            {
                // Unable to process import job and retries exceeded
                var download = await downloadRepository.GetByIdAsync(job.DownloadId);
                if (download == null)
                {
                    logger.LogError($"Download {job.DownloadId} disappeared after job {job.Id} failed");
                    return;
                }

                await downloadService.UpdateAsync(
                    download.Blocked(
                        "Unable to import the download",
                        $"See the log of job {job.Id} for more information"));
            }
        }

        /// <summary>
        /// Make sure the job is consistent, recover all related files and 
        /// send them for processing to IDownloadImportService
        /// </summary>
        /// <param name="job"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <exception cref="DownloadProcessingException"></exception>
        public async Task ProcessJobAsync(DownloadProcessingJob job, CancellationToken cancellationToken)
        {
            logger.LogInformation($"Processing job {job.Id} for download {job.DownloadId}: {job.JobType}");

            using var scope = scopeFactory.CreateScope();
            var downloadRepository = scope.ServiceProvider.GetRequiredService<IDownloadRepository>();
            var downloadProcessingJobService = scope.ServiceProvider.GetRequiredService<IDownloadProcessingJobService>();
            var download = await downloadRepository.GetByIdAsync(job.DownloadId);
            if (download == null)
            {
                throw new DownloadProcessingException($"The download {job.DownloadId} does not exist anymore");
            }

            var isEbook = DownloadContentTypes.IsEbook(download);
            var importMediaType = isEbook ? EditionMediaType.Ebook : EditionMediaType.Audiobook;

            // Extensions are configured per media type; an unset list falls back to the
            // built-in set for that media type rather than accepting anything.
            var importSettings = await scope.ServiceProvider
                .GetRequiredService<IConfigurationService>()
                .GetApplicationSettingsAsync();
            var allowedImportExtensions = isEbook
                ? importSettings?.AllowedEbookFileExtensions
                : importSettings?.AllowedFileExtensions;

            bool IsImportablePrimaryFile(string path) =>
                FileUtils.IsImportableFor(path, importMediaType, allowedImportExtensions);

            if (download.Status == DownloadStatus.Moved)
            {
                job.AddLogEntry($"Download {download.Id} is already imported; completing stale import job without work");
                await downloadProcessingJobService.UpdateJobAsync(job.MarkAsCompleted());
                return;
            }

            if (!download.AwaitsImportation())
            {
                throw new DownloadProcessingException($"Inconsistency: Download {download.Id} is not ready for importation. Current status: {download.Status}");
            }

            if (string.IsNullOrEmpty(download.DownloadPath))
            {
                throw new DownloadProcessingException($"Inconsistency: Download {download.Id} has no path set");
            }

            if (download.DownloadClientId == null)
            {
                throw new DownloadProcessingException($"Inconsistency: Download {download.Id} has no download client");
            }

            // Checked before the audiobook guard below: a collection carries no AudiobookId on
            // purpose, and everything past this point is book-shaped.
            if (download.IsCollectionPassthrough())
            {
                await ProcessCollectionPassthroughAsync(scope, job, download, downloadProcessingJobService, cancellationToken);
                return;
            }

            if (download.AudiobookId == null)
            {
                throw new DownloadProcessingException($"Inconsistency: Download {download.Id} has no audiobook related to it");
            }

            var audiobookRepository = scope.ServiceProvider.GetRequiredService<IAudiobookRepository>();
            var audiobook = await audiobookRepository.GetByIdAsync((int)download.AudiobookId);
            if (audiobook == null)
            {
                throw new DownloadProcessingException($"Inconsistency: Download {download.Id}'s audiobook {download.AudiobookId} cannot be retrieved");
            }

            var isDirectDownload = string.Equals(download.DownloadClientId, DirectDownloadMetadataKeys.ClientId, StringComparison.OrdinalIgnoreCase);
            DownloadClientConfiguration? client = null;
            if (!isDirectDownload)
            {
                var configurationService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
                client = await configurationService.GetDownloadClientConfigurationAsync(download.DownloadClientId);
                if (client == null)
                {
                    throw new DownloadProcessingException($"Inconsistency: Download {download.Id}'s client {download.DownloadClientId} cannot be retrieved");
                }
            }

            var downloadService = scope.ServiceProvider.GetRequiredService<IDownloadService>();
            var historyRepository = scope.ServiceProvider.GetRequiredService<IHistoryRepository>();
            var correlationId = job.GetOrCreateCorrelationId();

            await downloadService.UpdateAsync(download.Importing());
            await downloadProcessingJobService.UpdateJobAsync(job.MarkAsProcessing());
            await RecordHistoryAsync(
                historyRepository,
                download,
                audiobook,
                HistoryEvents.ImportStarted,
                HistoryOutcome.Requested,
                correlationId,
                $"Import attempt {job.RetryCount + 1} started",
                new Dictionary<string, object> { ["JobId"] = job.Id, ["Attempt"] = job.RetryCount + 1 },
                cancellationToken);

            if (!job.HasCheckpoint("FilesImported"))
            {
                if (isDirectDownload &&
                    (string.IsNullOrEmpty(download.DownloadPath) || (!File.Exists(download.DownloadPath) && !Directory.Exists(download.DownloadPath))))
                {
                    metrics.Increment("processing.source_missing");
                    await ScheduleRetryAsync(job, downloadProcessingJobService, historyRepository, download, audiobook,
                        correlationId, $"Direct-download source path not found at processing time: {download.DownloadPath}", cancellationToken, downloadService);
                    return;
                }

                QueueItem queueItem;
                List<string> files;
                try
                {
                    var downloadItemService = scope.ServiceProvider.GetRequiredService<IDownloadItemService>();
                    // External client DownloadPath may be stale or missing. Client-specific
                    // import resolvers own recovery from queue/history before the processor
                    // decides the source is unavailable.
                    queueItem = await downloadItemService.GetImportItemAsync(download, cancellationToken);
                    if (queueItem?.SourceFiles == null)
                    {
                        await ScheduleRetryAsync(job, downloadProcessingJobService, historyRepository, download, audiobook,
                            correlationId, isDirectDownload
                                ? "Unable to resolve the local direct-download file"
                                : "Unable to fetch the download from the download client", cancellationToken, downloadService);
                        return;
                    }

                    job.AddLogEntry($"Resolved {queueItem.SourceFiles.Count} file(s) for import");
                    files = [.. queueItem.SourceFiles.Where(File.Exists)];
                    job.AddLogEntry($"{files.Count} file(s) remaining after checking which ones are effectively on disk");

                    var missingFiles = queueItem.SourceFiles
                        .Where(path => !File.Exists(path))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    var archiveExtractor = scope.ServiceProvider.GetRequiredService<IArchiveExtractor>();
                    var missingPrimaryFiles = missingFiles
                        .Where(path => IsImportablePrimaryFile(path) || archiveExtractor.IsArchive(path))
                        .ToList();
                    var hasPrimaryFile = files.Any(path =>
                        IsImportablePrimaryFile(path) || archiveExtractor.IsArchive(path));

                    if (missingFiles.Count > 0 && missingPrimaryFiles.Count == 0 && hasPrimaryFile)
                    {
                        job.AddLogEntry(isEbook
                            ? $"Ignoring {missingFiles.Count} missing non-ebook companion file(s) reported by the download client"
                            : $"Ignoring {missingFiles.Count} missing non-audio companion file(s) reported by the download client");
                    }

                    // Retrying cannot fix a name the client mangled on write; see the partial.
                    if (missingPrimaryFiles.Count > 0
                        && await TryFlagNameMismatchAsync(
                            scope, job, download, missingPrimaryFiles, files, queueItem, downloadProcessingJobService, cancellationToken))
                    {
                        return;
                    }

                    if (files.Count == 0 || !hasPrimaryFile || missingPrimaryFiles.Count > 0)
                    {
                        var reason = files.Count == 0
                            ? DescribeMissingSourceFiles(queueItem.SourceFiles)
                            : missingPrimaryFiles.Count > 0
                                ? $"{(isEbook ? "Ebook" : "Audio")} or archive files reported by the download client are missing on disk"
                                : $"No {(isEbook ? "ebook" : "audio")} or archive files found on disk";
                        await ScheduleRetryAsync(job, downloadProcessingJobService, historyRepository, download, audiobook,
                            correlationId, reason, cancellationToken, downloadService);
                        return;
                    }
                }
                catch (DownloadProcessingException exception)
                {
                    await ScheduleRetryAsync(job, downloadProcessingJobService, historyRepository, download, audiobook,
                        correlationId, exception.Message, cancellationToken, downloadService);
                    return;
                }

                List<ImportResult> results;
                try
                {
                    var downloadImportService = scope.ServiceProvider.GetRequiredService<IDownloadImportService>();
                    var forceArchiveExtraction = isDirectDownload && string.Equals(
                        download.GetMetadataString(DirectDownloadMetadataKeys.RequiresArchiveExtraction),
                        bool.TrueString,
                        StringComparison.OrdinalIgnoreCase);
                    var importOptions = isEbook
                        ? new DownloadImportOptions(
                            ForceArchiveExtraction: forceArchiveExtraction,
                            ContentType: DownloadContentTypes.Ebook,
                            LibraryRoot: Environment.GetEnvironmentVariable("BOOKMARKARR_EBOOK_ROOT") ?? "/ebooks")
                        : forceArchiveExtraction
                            ? new DownloadImportOptions(ForceArchiveExtraction: true)
                            : null;
                    var primaryFiles = isEbook
                        ? files
                        : files.Where(path => !FileUtils.IsEbookFile(path)).ToList();
                    results = await downloadImportService.ImportDownloadFilesAsync(
                        audiobook,
                        primaryFiles,
                        cancellationToken,
                        importOptions);

                    // A legitimate audiobook bundle may also contain ebook payloads. Route those
                    // to the independently requested ebook edition instead of treating them as
                    // audiobook companions or registering them as audio files.
                    if (!isEbook)
                    {
                        var ebookFiles = files.Where(FileUtils.IsEbookFile).ToList();
                        var hasEbookEdition = audiobook.Editions.Any(e => e.MediaType == EditionMediaType.Ebook);
                        if (ebookFiles.Count > 0 && hasEbookEdition)
                        {
                            var ebookResults = await downloadImportService.ImportDownloadFilesAsync(
                                audiobook,
                                ebookFiles,
                                cancellationToken,
                                new DownloadImportOptions(
                                    ContentType: DownloadContentTypes.Ebook,
                                    LibraryRoot: Environment.GetEnvironmentVariable("BOOKMARKARR_EBOOK_ROOT") ?? "/ebooks"));
                            results.AddRange(ebookResults);
                        }
                    }
                }
                catch (InvalidOperationException exception)
                {
                    await FailImportAsync(job, downloadProcessingJobService, historyRepository, download, audiobook,
                        correlationId, exception.Message, cancellationToken);
                    return;
                }

                foreach (var result in results)
                {
                    if (!string.IsNullOrEmpty(result.Message)) job.AddLogEntry(result.Message);
                }

                if (results.Any(result => !result.Success))
                {
                    await FailImportAsync(job, downloadProcessingJobService, historyRepository, download, audiobook,
                        correlationId, "Unable to import at least one file for the job (see the log entries)", cancellationToken);
                    return;
                }

                // Persist edition file ownership separately from the inherited audiobook scan.
                // This is also the hard boundary preventing audio from being registered to ebook
                // editions and ebook formats from being registered as audio.
                var editionDb = scope.ServiceProvider.GetRequiredService<BookmarkarrDbContext>();
                var editions = await editionDb.BookEditions.Include(e => e.Files)
                    .Where(e => e.BookId == audiobook.Id).ToListAsync(cancellationToken);
                foreach (var result in results.Where(r => r.Success && !string.IsNullOrWhiteSpace(r.FinalPath)))
                {
                    var mediaType = FileUtils.IsEbookFile(result.FinalPath!) ? EditionMediaType.Ebook
                        : FileUtils.IsAudioFile(result.FinalPath!) ? EditionMediaType.Audiobook
                        : (EditionMediaType?)null;
                    if (mediaType is null) continue;
                    var edition = editions.FirstOrDefault(e => e.MediaType == mediaType.Value);
                    if (edition is null) continue;

                    // Registering the file and marking the edition imported are deliberately
                    // separate. They used to share one guard, so an already-registered path skipped
                    // both — and a retry, a reprocess, or any second pass over the same files left
                    // the edition pinned at Downloading with its files sitting on disk. The edition
                    // then still counted as wanted, so it stayed in Wanted forever and remained
                    // eligible to be downloaded again.
                    var alreadyRegistered = edition.Files.Any(
                        f => string.Equals(f.Path, result.FinalPath, StringComparison.OrdinalIgnoreCase));

                    if (!alreadyRegistered)
                    {
                        edition.Files.Add(new EditionFile
                        {
                            Path = Path.GetFullPath(result.FinalPath!),
                            Extension = Path.GetExtension(result.FinalPath!).ToLowerInvariant(),
                            Size = File.Exists(result.FinalPath) ? new FileInfo(result.FinalPath).Length : 0
                        });
                    }

                    // A successful import is what makes an edition imported, whether or not this
                    // particular pass was the one that added the row.
                    MarkEditionImported(edition, importSettings);
                }
                await editionDb.SaveChangesAsync(cancellationToken);

                var wasRegisteredToAudiobook = results.Any(result => result.WasRegisteredToAudiobook);
                if (!isEbook && !wasRegisteredToAudiobook)
                {
                    var audiobookFileRepository = scope.ServiceProvider.GetRequiredService<IAudiobookFileRepository>();
                    var existingAudiobookFiles = await audiobookFileRepository.GetByAudiobookIdAsync(audiobook.Id, cancellationToken);
                    if (existingAudiobookFiles.Count <= 0)
                    {
                        await FailImportAsync(job, downloadProcessingJobService, historyRepository, download, audiobook,
                            correlationId, "No audio files were registered after file import", cancellationToken);
                        return;
                    }
                }

                var importedPath = results
                    .FirstOrDefault(result => result.Success && !string.IsNullOrWhiteSpace(result.FinalPath))
                    ?.FinalPath;
                if (!string.IsNullOrWhiteSpace(importedPath))
                {
                    download.FinalPath = importedPath;
                    await downloadService.UpdateAsync(download);
                }

                foreach (var result in results)
                {
                    var outcome = string.IsNullOrWhiteSpace(result.SourcePath) || string.IsNullOrWhiteSpace(result.FinalPath)
                        ? HistoryOutcome.Skipped
                        : HistoryOutcome.Succeeded;
                    var eventType = outcome == HistoryOutcome.Skipped
                        ? HistoryEvents.FileSkipped
                        : result.Action == Bookmarkarr.Domain.Audiobooks.Enumerations.FileAction.Move
                            ? HistoryEvents.FileMoved
                            : HistoryEvents.FileCopied;
                    await historyRepository.AddAsync(new History
                    {
                        AudiobookId = audiobook.Id,
                        EditionId = editions.FirstOrDefault(e =>
                            FileUtils.IsEbookFile(result.FinalPath ?? result.SourcePath ?? string.Empty)
                                ? e.MediaType == EditionMediaType.Ebook
                                : e.MediaType == EditionMediaType.Audiobook)?.Id,
                        MediaType = FileUtils.IsEbookFile(result.FinalPath ?? result.SourcePath ?? string.Empty) ? "ebook" : "audiobook",
                        AudiobookTitle = audiobook.Title,
                        SourceTitle = Path.GetFileName(result.FinalPath ?? result.SourcePath ?? download.Title),
                        DownloadId = download.Id.ToUpperInvariant(),
                        DownloadClientId = download.DownloadClientId,
                        EventType = eventType,
                        Outcome = outcome,
                        Source = "DownloadImport",
                        Message = result.Message ?? $"{result.Action} completed",
                        Timestamp = DateTime.UtcNow,
                        CorrelationId = correlationId,
                        Data = JsonSerializer.Serialize(new
                        {
                            JobId = job.Id,
                            result.Action,
                            result.SourcePath,
                            result.FinalPath,
                            result.WasRegisteredToAudiobook
                        })
                    }, cancellationToken);
                }

                job.SetCheckpoint("FilesImported", results.Count);
                await downloadProcessingJobService.UpdateJobAsync(job);
            }

            await CompleteImportAsync(scope.ServiceProvider, scanQueueService, job, downloadProcessingJobService,
                historyRepository, download, audiobook, client, correlationId, isDirectDownload, isEbook, cancellationToken);
        }
    }
}
