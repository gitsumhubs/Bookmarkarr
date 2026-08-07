/*
 * Bookmarkarr - Audiobook Management System
 * Copyright (C) 2024-2026 Bookmarkarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Bookmarkarr.Application.Audiobooks.Contracts.Repositories;
using Bookmarkarr.Application.Configuration.Contracts;
using Bookmarkarr.Application.Downloads.Contracts;
using Bookmarkarr.Application.Downloads.Import;
using Bookmarkarr.Application.Downloads.Submission;
using Bookmarkarr.Domain.Books;
using Bookmarkarr.Domain.Downloads;
using Microsoft.Extensions.DependencyInjection;
using Bookmarkarr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bookmarkarr.Infrastructure.Downloads.Processing
{
    public partial class DownloadProcessingJobProcessor
    {
        /// <summary>
        /// Finishes a collection grab: place the release at the library root, untouched.
        /// </summary>
        /// <remarks>
        /// Runs instead of the book-shaped import, not alongside it. A collection row carries no
        /// <c>AudiobookId</c> by design, so every step below the ordinary audiobook lookup —
        /// naming, file registration, edition status, history — has no book to act on and is
        /// skipped rather than fabricated.
        /// </remarks>
        private async Task ProcessCollectionPassthroughAsync(
            IServiceScope scope,
            DownloadProcessingJob job,
            Download download,
            IDownloadProcessingJobService jobService,
            CancellationToken cancellationToken)
        {
            var downloadService = scope.ServiceProvider.GetRequiredService<IDownloadService>();
            await downloadService.UpdateAsync(download.Importing());
            await jobService.UpdateJobAsync(job.MarkAsProcessing());

            var destinationRoot = await ResolveCollectionRootAsync(scope);
            if (string.IsNullOrWhiteSpace(destinationRoot))
            {
                job.AddLogEntry("No audiobook root folder is configured; cannot place a collection");
                await downloadService.UpdateAsync(download.Failed("No audiobook root folder configured"));
                await jobService.UpdateJobAsync(job.MarkAsFailed("No audiobook root folder configured"));
                return;
            }

            var itemService = scope.ServiceProvider.GetRequiredService<IDownloadItemService>();
            var queueItem = await itemService.GetImportItemAsync(download, cancellationToken);
            var files = (queueItem?.SourceFiles ?? []).Where(File.Exists).ToList();
            if (files.Count == 0)
            {
                job.AddLogEntry("No files found on disk for the collection download");
                await downloadService.UpdateAsync(download.Failed("No files found on disk"));
                await jobService.UpdateJobAsync(job.MarkAsFailed("No files found on disk"));
                return;
            }

            var settings = await scope.ServiceProvider
                .GetRequiredService<IConfigurationService>()
                .GetApplicationSettingsAsync();

            var importer = scope.ServiceProvider.GetRequiredService<CollectionPassthroughImporter>();
            var result = await importer.PlaceAsync(files, destinationRoot, settings.CompletedFileAction, cancellationToken);

            job.AddLogEntry(
                $"Collection passthrough placed {result.Placed} file(s) under {result.DestinationRoot}" +
                (result.Failed > 0 ? $", {result.Failed} failed" : string.Empty));

            if (result.Placed == 0)
            {
                await downloadService.UpdateAsync(download.Failed("No collection files could be placed"));
                await jobService.UpdateJobAsync(job.MarkAsFailed("No collection files could be placed"));
                return;
            }

            download.FinalPath = result.DestinationRoot;
            await downloadService.UpdateAsync(download.Imported());
            await jobService.UpdateJobAsync(job.MarkAsCompleted());

            await MarkSourceBookFinishedAsync(scope, download, job, cancellationToken);

            // No library scan is queued: the scan queue is keyed by an Audiobook, and a collection
            // has none by design. Audiobookshelf indexes the same shared path on its own schedule,
            // which is the point of placing the files this way; bringing them into Bookmarkarr's
            // own library is left to a manual scan or adopt-scan, where folder-derived identities
            // are reported for review rather than committed as guesses.
            logger.LogInformation(
                "Collection download {DownloadId} placed under {Root}; run a library scan or adopt-scan to track its books",
                download.Id,
                result.DestinationRoot);
        }

        /// <summary>
        /// Marks the book the collection was searched from as finished, so it leaves Wanted.
        /// </summary>
        /// <remarks>
        /// Chosen behaviour, not an inference: nothing here verifies the collection actually
        /// contained that book, because the placed folders are untracked until a scan and matching
        /// them by name is the guesswork adoption reports for review rather than committing. A
        /// collection that did not include the book therefore marks it finished anyway, and it will
        /// not be searched for again — clearing the edition's status by hand is the way back.
        ///
        /// Failures are logged and swallowed: the files are already placed and the download is
        /// already complete, so a bookkeeping problem must not fail an import that succeeded.
        /// </remarks>
        private async Task MarkSourceBookFinishedAsync(
            IServiceScope scope,
            Download download,
            DownloadProcessingJob job,
            CancellationToken cancellationToken)
        {
            var sourceBookId = download.SourceAudiobookId();
            if (sourceBookId is null)
            {
                return;
            }

            try
            {
                var db = scope.ServiceProvider.GetRequiredService<BookmarkarrDbContext>();
                var editions = await db.BookEditions
                    .Where(e => e.BookId == sourceBookId.Value && e.MediaType == EditionMediaType.Audiobook)
                    .ToListAsync(cancellationToken);

                if (editions.Count == 0)
                {
                    job.AddLogEntry($"Collection source book {sourceBookId} has no audiobook edition to mark finished");
                    return;
                }

                foreach (var edition in editions)
                {
                    edition.Status = EditionWantedStatus.Imported;
                    edition.UpdatedAt = DateTime.UtcNow;
                }

                await db.SaveChangesAsync(cancellationToken);
                job.AddLogEntry($"Marked source book {sourceBookId} finished after collection import");
                logger.LogInformation(
                    "Collection download {DownloadId} marked source book {BookId} as imported without verifying the collection contains it",
                    download.Id,
                    sourceBookId.Value);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(
                    ex,
                    "Collection download {DownloadId} placed successfully but could not mark source book {BookId} finished",
                    download.Id,
                    sourceBookId.Value);
            }
        }

        /// <summary>Default audiobook root, falling back to any audiobook root, then the output path.</summary>
        private static async Task<string?> ResolveCollectionRootAsync(IServiceScope scope)
        {
            var rootFolders = await scope.ServiceProvider
                .GetRequiredService<IRootFolderRepository>()
                .GetAllAsync();

            var audiobookRoots = rootFolders
                .Where(root => root.MediaType == EditionMediaType.Audiobook && !string.IsNullOrWhiteSpace(root.Path))
                .ToList();

            var chosen = audiobookRoots.FirstOrDefault(root => root.IsDefault) ?? audiobookRoots.FirstOrDefault();
            if (chosen != null)
            {
                return chosen.Path;
            }

            var settings = await scope.ServiceProvider
                .GetRequiredService<IConfigurationService>()
                .GetApplicationSettingsAsync();

            return string.IsNullOrWhiteSpace(settings.OutputPath) ? null : settings.OutputPath;
        }
    }
}
