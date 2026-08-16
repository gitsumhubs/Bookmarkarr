/*
 * Bookmarkarr - Audiobook Management System
 * Copyright (C) 2024-2026 Bookmarkarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

namespace Bookmarkarr.Infrastructure.Downloads.Processing
{
    public partial class DownloadProcessingJobProcessor
    {
        /// <summary>
        /// Explains why nothing on the client's file list was on disk.
        ///
        /// "No importable files found" is literally true whenever the list comes back empty, but it
        /// sends the reader hunting for missing files when the usual cause is that Bookmarkarr cannot
        /// see the directory at all. A client configured to report host paths rather than its own
        /// container paths (RDT Client's MappedPath is the common case) hands over a path that does
        /// not resolve inside this container, so every file reads as missing and the directory itself
        /// is absent. Naming that case points at the remote path mapping instead of at the files,
        /// which is where the fix actually lives.
        /// </summary>
        internal static string DescribeMissingSourceFiles(IReadOnlyCollection<string> sourceFiles)
        {
            if (sourceFiles.Count == 0)
            {
                return "The download client reported no files for this download";
            }

            var directories = sourceFiles
                .Select(Path.GetDirectoryName)
                .Where(directory => !string.IsNullOrWhiteSpace(directory))
                .Select(directory => directory!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Only claim a mapping problem when every directory is unreachable. A partially visible
            // set means the paths do resolve and something else removed the files.
            if (directories.Count > 0 && directories.TrueForAll(directory => !Directory.Exists(directory)))
            {
                return $"Directory not found: {directories[0]} — the path reported by the download client " +
                    "does not exist inside Bookmarkarr. Check the Remote Path Mappings for this client.";
            }

            return "No importable files found";
        }
    }
}
