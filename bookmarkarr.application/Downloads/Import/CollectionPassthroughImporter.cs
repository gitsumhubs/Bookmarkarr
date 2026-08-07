/*
 * Bookmarkarr - Audiobook Management System
 * Copyright (C) 2024-2026 Bookmarkarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Bookmarkarr.Application.Downloads.Contracts;
using Bookmarkarr.Domain.Audiobooks.Enumerations;
using Bookmarkarr.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Bookmarkarr.Application.Downloads.Import
{
    /// <summary>
    /// Places a collection download at the library root with its own folder structure intact.
    /// </summary>
    /// <remarks>
    /// The ordinary import path is book-shaped: it flattens every audio file into one book's
    /// folder and renames each by that book's naming pattern. Applied to a collection that
    /// produces a single Audiobookshelf item with hundreds of tracks, because Audiobookshelf maps
    /// one folder to one item and cannot split a folder back apart.
    ///
    /// This importer therefore does the opposite of the normal path on both counts: it preserves
    /// the release's relative directory layout, and it preserves filenames. What lands is what the
    /// indexer published, so Audiobookshelf's scanner sees the collection's own
    /// <c>Author/Title</c> folders and identifies each book separately.
    ///
    /// Nothing here writes to the library database. These books are unknown to Bookmarkarr until a
    /// library scan or adopt-scan picks them up, which is deliberate — inventing book rows from
    /// folder names is exactly the guesswork adoption reports as <c>NeedsReview</c> rather than
    /// committing silently.
    /// </remarks>
    public sealed class CollectionPassthroughImporter(
        IFileMover fileMover,
        ILogger<CollectionPassthroughImporter> logger)
    {
        public sealed record Result(int Placed, int Failed, string DestinationRoot);

        public async Task<Result> PlaceAsync(
            IReadOnlyList<string> sourceFiles,
            string destinationRoot,
            FileAction action,
            CancellationToken ct = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(destinationRoot);

            if (sourceFiles.Count == 0)
            {
                return new Result(0, 0, destinationRoot);
            }

            // Relative layout is measured from the release's own common root, so the download's
            // own containing folder is reproduced under the library root rather than its absolute
            // path from the download client.
            var sourceRoot = FileUtils.GetCommonDirectory(sourceFiles);
            var placed = 0;
            var failed = 0;

            foreach (var file in sourceFiles)
            {
                ct.ThrowIfCancellationRequested();

                var relative = !string.IsNullOrWhiteSpace(sourceRoot)
                    ? Path.GetRelativePath(sourceRoot, file)
                    : Path.GetFileName(file);

                // A release controls its own path strings, so a crafted entry could otherwise
                // climb out of the library root. Resolve and verify containment before writing.
                var destination = Path.GetFullPath(Path.Combine(destinationRoot, relative));
                var rootFull = Path.GetFullPath(destinationRoot);
                if (!destination.StartsWith(
                        rootFull.EndsWith(Path.DirectorySeparatorChar) ? rootFull : rootFull + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogWarning(
                        "Blocked collection file resolving outside the library root. Source {Source}, Relative {Relative}, Root {Root}",
                        file,
                        relative,
                        destinationRoot);
                    failed++;
                    continue;
                }

                // The destination directory is created by the mover, which owns filesystem access;
                // doing it here would put IO in the application layer, which is enforced against.
                if (await fileMover.PerformActionOn(action, file, destination))
                {
                    placed++;
                }
                else
                {
                    failed++;
                    logger.LogWarning("Failed to place collection file {Source} at {Destination}", file, destination);
                }
            }

            logger.LogInformation(
                "Collection passthrough placed {Placed} file(s) under {Root}, {Failed} failed",
                placed,
                destinationRoot,
                failed);

            return new Result(placed, failed, destinationRoot);
        }
    }
}
