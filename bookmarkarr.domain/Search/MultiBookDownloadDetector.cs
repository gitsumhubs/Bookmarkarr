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

using System.Text.RegularExpressions;

namespace Bookmarkarr.Domain.Search
{
    /// <summary>
    /// Looks at a finished download that is about to be filed under one book and says when it is
    /// plainly more than one.
    /// </summary>
    /// <remarks>
    /// Import renames every audio file to the book's title and registers it to that book, which is
    /// right for one book in many files and wrong for a series pack: twelve books became 676 files
    /// of "one" book. Two signs are trusted. The release was named as a pack, or its audio sits in
    /// several folders that are not discs or parts. A single folder of loose files cannot be told
    /// apart from a long book, and is left alone.
    /// </remarks>
    public static partial class MultiBookDownloadDetector
    {
        private static readonly HashSet<string> NotBooks = new(StringComparer.OrdinalIgnoreCase)
        {
            "extras", "extra", "bonus", "cover", "covers", "artwork", "art", "scans", "sample", "samples", "images", "booklet"
        };

        /// <summary>Why the download looks like several books, or null when it does not.</summary>
        /// <param name="audioFiles">Full paths of the audio files that would be imported.</param>
        public static string? Detect(WantedBook wanted, string? releaseTitle, IReadOnlyCollection<string> audioFiles)
        {
            var identity = WantedBookIdentity.From(wanted);
            var marker = ReleasePackDetector.FindPackMarker(ReleaseNameTokenizer.Fold(releaseTitle), identity.OwnWords);
            if (marker != null)
            {
                return $"the release is named as several books (\"{marker}\")";
            }

            var folders = BookFolders(audioFiles);
            if (folders.Count >= 3 || (folders.Count == 2 && folders.All(folder => NumberedBookPattern().IsMatch(folder))))
            {
                var shown = string.Join(", ", folders.Take(3).Select(folder => $"\"{folder}\""));
                return $"its audio is in {folders.Count} separate folders ({shown}{(folders.Count > 3 ? ", …" : string.Empty)})";
            }

            return null;
        }

        /// <summary>
        /// The folders directly under the download's common root that hold audio and are not a
        /// disc, a part or an extras folder.
        /// </summary>
        private static List<string> BookFolders(IReadOnlyCollection<string> audioFiles)
        {
            var paths = audioFiles
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
                .ToList();
            if (paths.Count < 2)
            {
                return [];
            }

            var shared = 0;
            var shortest = paths.Min(parts => parts.Length) - 1;
            while (shared < shortest && paths.All(parts => string.Equals(parts[shared], paths[0][shared], StringComparison.OrdinalIgnoreCase)))
            {
                shared++;
            }

            return paths
                .Where(parts => parts.Length > shared + 1)
                .Select(parts => parts[shared])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(folder => !NotBooks.Contains(folder.Trim()) && !DiscOrPartPattern().IsMatch(folder))
                .ToList();
        }

        // "CD 2", "Disc 03", "Part 1 of 6", "Chapter 12", or a bare number: pieces of one book.
        [GeneratedRegex(@"^\W*(?:(?:cd|disc|disk|part|pt|side|tape|chapter|section|act)\b\W*\d{1,3}\b.*|\d{1,3})\W*$", RegexOptions.IgnoreCase)]
        private static partial Regex DiscOrPartPattern();

        // "Book 1 - …", "Bk 02", "Vol. 3", "#4": a folder that numbers itself as a book.
        [GeneratedRegex(@"(?:\b(?:book|bk|vol|volume)\b\W*|#)\d{1,3}\b", RegexOptions.IgnoreCase)]
        private static partial Regex NumberedBookPattern();
    }
}
