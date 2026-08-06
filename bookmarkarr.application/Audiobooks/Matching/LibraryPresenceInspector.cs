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

using Bookmarkarr.Application.Common;
using Bookmarkarr.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Bookmarkarr.Application.Audiobooks.Matching
{
    /// <summary>What a book's destination directory actually contains right now.</summary>
    /// <param name="BasePath">The resolved per-book directory, or null when one could not be derived.</param>
    /// <param name="AudioFileCount">Audio files found beneath <paramref name="BasePath"/>.</param>
    /// <param name="BestQuality">
    /// Best profile rung the on-disk files map to, or null when none of them match the profile.
    /// Derived from file extension only, so it is deliberately pessimistic — see
    /// <see cref="LibraryPresenceInspector"/>.
    /// </param>
    public readonly record struct LibraryPresence(string? BasePath, int AudioFileCount, string? BestQuality)
    {
        /// <summary>True when the book already has audio on disk.</summary>
        public bool HasFiles => AudioFileCount > 0;

        /// <summary>Nothing was found, either because no path resolved or the directory was empty.</summary>
        public static LibraryPresence None(string? basePath = null) => new(basePath, 0, null);
    }

    /// <summary>
    /// Answers "is this book already on disk?" by looking at the filesystem rather than at
    /// database rows.
    ///
    /// Bookmarkarr's own tables are an incomplete picture of the library: files imported before a
    /// book was tracked, files whose import failed after the bytes landed, and folders written by
    /// anything else pointed at the same root all exist on disk with no matching
    /// <c>AudiobookFiles</c> row. Deciding "do I already have this?" from rows alone therefore
    /// re-downloads books that are sitting in the library already.
    /// </summary>
    public sealed class LibraryPresenceInspector(
        IFileSystem fileSystem,
        IFileNamingService fileNamingService,
        ILogger logger)
    {
        /// <summary>
        /// Enumeration is capped because a mis-derived path can resolve to a library root holding
        /// thousands of files. The count only has to answer "any?", so a low cap costs nothing.
        /// </summary>
        private const int MaxFilesInspected = 512;

        /// <summary>
        /// Inspects the audiobook edition's destination directory.
        ///
        /// Scoped to audio deliberately: automatic search runs per audiobook against an audio
        /// quality profile, so an ebook sitting in the same tree must not suppress an audio grab.
        /// </summary>
        public LibraryPresence Inspect(Audiobook audiobook, ApplicationSettings settings)
        {
            if (!AudiobookPathPlanner.TryResolveBasePath(audiobook, settings, fileNamingService, out var basePath)
                || string.IsNullOrWhiteSpace(basePath))
            {
                return LibraryPresence.None();
            }

            // A path that collapsed to the library root is not a per-book directory. Scanning it
            // would report every book in the library as "already have it" for whichever book is
            // being evaluated.
            if (IsLibraryRoot(audiobook, settings, basePath))
            {
                logger.LogDebug(
                    "Resolved base path for audiobook {AudiobookId} is a library root ({BasePath}); treating as no on-disk presence",
                    audiobook.Id,
                    basePath);
                return LibraryPresence.None(basePath);
            }

            if (!fileSystem.DirectoryExists(basePath))
            {
                return LibraryPresence.None(basePath);
            }

            try
            {
                var audioFiles = fileSystem
                    .EnumerateFiles(basePath, "*", SearchOption.AllDirectories)
                    .Where(FileUtils.IsAudioFile)
                    .Take(MaxFilesInspected)
                    .ToList();

                if (audioFiles.Count == 0)
                {
                    return LibraryPresence.None(basePath);
                }

                return new LibraryPresence(basePath, audioFiles.Count, ResolveBestQuality(audioFiles, audiobook.QualityProfile));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // An unreadable directory must not decide the question either way; report absence
                // so search behaves exactly as it did before this check existed.
                logger.LogWarning(
                    ex,
                    "Could not inspect {BasePath} for audiobook {AudiobookId}; proceeding as if no files are present",
                    basePath,
                    audiobook.Id);
                return LibraryPresence.None(basePath);
            }
        }

        /// <summary>
        /// Best rung the files map to, using extension alone.
        ///
        /// No bitrate is available without probing every file, and <see cref="QualityMatcher"/>
        /// rounds an unknown bitrate down to the worst matching rung. That pessimism is the point:
        /// a genuinely better release still scores as an upgrade, so this check suppresses
        /// duplicates without ever suppressing an upgrade.
        /// </summary>
        private static string? ResolveBestQuality(IEnumerable<string> paths, QualityProfile? profile)
        {
            string? best = null;

            foreach (var label in paths
                .Select(path => QualityMatcher.MatchLabel(
                    new AudioQualityInput { Path = path, Format = Path.GetExtension(path).TrimStart('.') },
                    profile))
                .Where(label => !string.IsNullOrEmpty(label)))
            {
                if (best == null || QualityMatcher.IsLabelBetter(label, best, profile))
                {
                    best = label;
                }
            }

            return best;
        }

        private static bool IsLibraryRoot(Audiobook audiobook, ApplicationSettings settings, string basePath)
        {
            var edition = audiobook.Editions
                .FirstOrDefault(candidate => candidate.MediaType == EditionMediaType.Audiobook);

            return Matches(edition?.RootFolder?.Path)
                || Matches(edition?.RootPath)
                || Matches(settings.OutputPath);

            bool Matches(string? root) =>
                !string.IsNullOrWhiteSpace(root)
                && string.Equals(Normalize(root), Normalize(basePath), StringComparison.OrdinalIgnoreCase);

            static string Normalize(string path) =>
                FileUtils.NormalizeStoredPath(path)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
