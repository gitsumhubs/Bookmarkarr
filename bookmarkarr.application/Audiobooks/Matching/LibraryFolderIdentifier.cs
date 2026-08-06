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
 */

using System.Text.RegularExpressions;
using Bookmarkarr.Domain.Common;

namespace Bookmarkarr.Application.Audiobooks.Matching
{
    /// <summary>How the identity of a folder was established.</summary>
    public enum FolderIdentitySource
    {
        /// <summary>Nothing usable could be derived.</summary>
        None,

        /// <summary>Folder and file naming only — a guess, and often wrong on messy trees.</summary>
        FolderName,

        /// <summary>Audiobookshelf's completed match, without an ASIN.</summary>
        Audiobookshelf,

        /// <summary>Audiobookshelf's match including an ASIN — the only exact key available.</summary>
        AudiobookshelfAsin
    }

    /// <summary>What a folder on disk appears to contain.</summary>
    public sealed record FolderIdentity(
        string Path,
        string? Title,
        string? Author,
        string? Series,
        string? SeriesNumber,
        string? Asin,
        string? Year,
        FolderIdentitySource Source)
    {
        /// <summary>
        /// Whether this is trustworthy enough to create a book from without review.
        ///
        /// Only Audiobookshelf matches qualify. A folder name is a guess: the same tree routinely
        /// mixes <c>Author/Title</c>, <c>Series/Title</c>, publisher-as-author, and bare titles,
        /// and no rule reads all of them correctly.
        /// </summary>
        public bool IsConfident => Source is FolderIdentitySource.Audiobookshelf
            or FolderIdentitySource.AudiobookshelfAsin;

        public static FolderIdentity Unknown(string path) =>
            new(path, null, null, null, null, null, null, FolderIdentitySource.None);
    }

    /// <summary>
    /// Works out which book a library folder holds.
    ///
    /// Audiobookshelf is consulted first because it has already matched these exact folders
    /// against Audible and holds an ASIN — an exact key, where title matching is guesswork.
    /// Folder naming is the fallback, and anything it produces is marked for review rather than
    /// adopted silently.
    /// </summary>
    public static class LibraryFolderIdentifier
    {
        // Trailing decorations a folder name picks up: "(Unabridged)", "[2019]", "{M4B}".
        private static readonly Regex DecorationPattern =
            new(@"[\(\[\{][^\)\]\}]*[\)\]\}]", RegexOptions.Compiled);

        // "Book 3", "#3", "- 03 -" style position markers.
        private static readonly Regex SeriesNumberPattern =
            new(@"(?:^|[\s,\-])(?:book\s*|#)(\d{1,3})(?:$|[\s,\-])", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex YearPattern = new(@"(?:^|\D)(19|20)(\d{2})(?:\D|$)", RegexOptions.Compiled);

        /// <summary>
        /// Identifies one folder, preferring Audiobookshelf's answer.
        /// </summary>
        /// <param name="folderPath">Absolute path of the book folder.</param>
        /// <param name="rootPath">Library root, used to read the folder's position in the tree.</param>
        /// <param name="audiobookshelf">Path-indexed Audiobookshelf items; may be empty.</param>
        public static FolderIdentity Identify(
            string folderPath,
            string rootPath,
            IReadOnlyDictionary<string, AudiobookshelfItem> audiobookshelf)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return FolderIdentity.Unknown(folderPath ?? string.Empty);
            }

            var normalized = Normalize(folderPath);

            if (audiobookshelf.Count > 0 && audiobookshelf.TryGetValue(normalized, out var item))
            {
                return new FolderIdentity(
                    folderPath,
                    item.Title,
                    item.Authors.FirstOrDefault(),
                    item.Series,
                    item.SeriesSequence,
                    item.Asin,
                    item.PublishedYear,
                    string.IsNullOrWhiteSpace(item.Asin)
                        ? FolderIdentitySource.Audiobookshelf
                        : FolderIdentitySource.AudiobookshelfAsin);
            }

            return FromFolderName(folderPath, rootPath);
        }

        /// <summary>
        /// Last-resort reading of the path itself.
        ///
        /// Treats the segment above the book folder as the author, which is right for
        /// <c>Author/Title</c> and wrong for <c>Series/Title</c> — hence review, never adoption.
        /// </summary>
        internal static FolderIdentity FromFolderName(string folderPath, string rootPath)
        {
            var normalized = Normalize(folderPath);
            var root = Normalize(rootPath);

            var relative = normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? normalized[root.Length..].Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                : normalized.Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            var segments = relative
                .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length == 0)
            {
                return FolderIdentity.Unknown(folderPath);
            }

            var rawTitle = segments[^1];
            var author = segments.Length >= 2 ? Clean(segments[^2]) : null;

            // Year is read from the raw name, because it usually lives inside a decoration that
            // the next step removes.
            var yearMatch = YearPattern.Match(rawTitle);
            var year = yearMatch.Success ? $"{yearMatch.Groups[1].Value}{yearMatch.Groups[2].Value}" : null;

            // Decorations go first, then the series marker is matched against the *result*.
            // Matching the raw name and replacing the literal afterwards silently fails, because
            // stripping decorations changes the surrounding whitespace the match captured.
            var withoutDecorations = Clean(DecorationPattern.Replace(rawTitle, " "));

            var seriesMatch = SeriesNumberPattern.Match(withoutDecorations);
            var seriesNumber = seriesMatch.Success ? seriesMatch.Groups[1].Value : null;

            var title = seriesMatch.Success
                ? Clean(SeriesNumberPattern.Replace(withoutDecorations, " "))
                : withoutDecorations;

            return new FolderIdentity(
                folderPath,
                string.IsNullOrWhiteSpace(title) ? Clean(rawTitle) : title,
                author,
                null,
                seriesNumber,
                null,
                year,
                FolderIdentitySource.FolderName);
        }

        private static string Clean(string value) =>
            Regex.Replace(value ?? string.Empty, @"[\s_]+", " ")
                .Trim(' ', '-', ',', '.', '_');

        private static string Normalize(string path) =>
            FileUtils.NormalizeStoredPath(path ?? string.Empty)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
