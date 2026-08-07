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
using Bookmarkarr.Domain.Books;
using Bookmarkarr.Domain.Downloads;
using Microsoft.Extensions.DependencyInjection;
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
