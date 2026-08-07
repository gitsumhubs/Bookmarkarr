/*
 * Bookmarkarr - Audiobook Management System
 * Copyright (C) 2024-2026 Bookmarkarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using System.Text;

namespace Bookmarkarr.Application.Downloads.Import
{
    /// <summary>
    /// Pairs a file the client reported with the differently-named file it actually wrote.
    /// </summary>
    /// <param name="ReportedPath">The path the download client claims exists.</param>
    /// <param name="ActualPath">The file on disk believed to be the same payload.</param>
    public sealed record ImportNameMismatch(string ReportedPath, string ActualPath);

    /// <summary>
    /// Finds files a client renamed on write, so an import can be repaired instead of retried.
    /// </summary>
    /// <remarks>
    /// Some clients sanitize filenames as they write them and then keep reporting the original.
    /// RDT Client strips dots — a chapter named <c>Ash and Elm....mp3</c> lands as
    /// <c>Ash and Elmmp3</c>, losing even its extension — and spaces out apostrophes. The import
    /// then looks for a name that will never exist, so retrying is pure delay: the payload is
    /// present and correct, only its name disagrees.
    ///
    /// Matching is deliberately conservative, because a wrong pairing imports the wrong audio
    /// under a book's name and is near-impossible to notice afterwards:
    ///   - candidates must sit in the same directory as the reported file;
    ///   - comparison ignores only characters clients are known to drop, by reducing both names to
    ///     their letters and digits;
    ///   - a reduced form matching more than one candidate is discarded rather than guessed at;
    ///   - files the client accounted for elsewhere are never offered as candidates.
    ///
    /// Nothing here renames anything. It reports what it believes, and a person confirms.
    /// </remarks>
    public static class ImportNameMismatchDetector
    {
        public static IReadOnlyList<ImportNameMismatch> Detect(
            IReadOnlyList<string> reportedMissing,
            IReadOnlyList<string> reportedPresent,
            IReadOnlyList<string> filesOnDisk)
        {
            if (reportedMissing.Count == 0 || filesOnDisk.Count == 0)
            {
                return [];
            }

            // Anything the client already matched to a real file is spoken for and must not be
            // offered as the answer to a different missing entry.
            var claimed = new HashSet<string>(reportedPresent, StringComparer.OrdinalIgnoreCase);

            var unclaimedByDirectory = filesOnDisk
                .Where(path => !claimed.Contains(path))
                .GroupBy(path => Path.GetDirectoryName(path) ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

            var matches = new List<ImportNameMismatch>();

            foreach (var missing in reportedMissing)
            {
                var directory = Path.GetDirectoryName(missing) ?? string.Empty;
                if (!unclaimedByDirectory.TryGetValue(directory, out var candidates))
                {
                    continue;
                }

                var target = Reduce(Path.GetFileName(missing));
                if (target.Length == 0)
                {
                    continue;
                }

                var hits = candidates
                    .Where(candidate => string.Equals(Reduce(Path.GetFileName(candidate)), target, StringComparison.Ordinal))
                    .ToList();

                // Exactly one, or it stays unresolved. An ambiguous guess is worse than no guess.
                if (hits.Count == 1)
                {
                    matches.Add(new ImportNameMismatch(missing, hits[0]));
                    candidates.Remove(hits[0]);
                }
            }

            return matches;
        }

        /// <summary>Letters and digits only, lowercased — what survives any client's sanitizing.</summary>
        private static string Reduce(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(name.Length);
            foreach (var character in name)
            {
                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(char.ToLowerInvariant(character));
                }
            }

            return builder.ToString();
        }
    }
}
