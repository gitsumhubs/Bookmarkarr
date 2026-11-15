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

namespace Bookmarkarr.Domain.Audiobooks.Rules
{
    public static class MultiFileImportPlanner
    {
        private static readonly Regex NumericChunkPattern = new(@"\d+|\D+", RegexOptions.Compiled);
        private static readonly Regex DiskPattern = new(
            @"\b(?:disc|disk|cd|part|pt|volume|vol)\s*[-_. ]*0*(\d+)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ChapterPattern = new(
            @"\b(?:chapter|ch|track)\s*[-_. ]*0*(\d+)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex LeadingNumberPattern = new(
            @"^(?:track\s*)?0*(\d+)(?:[\s._-]|$)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex TrailingNumberPattern = new(
            @"(?:^|[\s._\-\(\[])\s*0*(\d+)\s*(?:[\)\]]|$)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static List<PlannedImportFile> BuildPlans(IEnumerable<(string FullPath, string? RelativePath)> inputs)
        {
            var distinct = new Dictionary<string, (string FullPath, string? RelativePath)>(StringComparer.OrdinalIgnoreCase);
            foreach (var input in inputs)
            {
                if (string.IsNullOrWhiteSpace(input.FullPath))
                {
                    continue;
                }

                if (!distinct.ContainsKey(input.FullPath))
                {
                    distinct[input.FullPath] = input;
                }
            }

            var ordered = distinct.Values
                .OrderBy(i => ToNaturalSortKey(GetComparablePath(i.RelativePath, i.FullPath)), StringComparer.Ordinal)
                .ThenBy(i => GetComparablePath(i.RelativePath, i.FullPath), StringComparer.OrdinalIgnoreCase)
                .ThenBy(i => i.FullPath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var plans = new List<PlannedImportFile>(ordered.Count);
            for (var index = 0; index < ordered.Count; index++)
            {
                var item = ordered[index];
                var (diskNumber, chapterNumber) = InferOrderHints(item.RelativePath, item.FullPath);

                if (ordered.Count > 1)
                {
                    // A file named only "Disc 2" numbers itself. The reverse is not true: a track
                    // number says nothing about which disc it is on, and copying it across is what
                    // produced "Title-210-210".
                    chapterNumber ??= diskNumber;
                    chapterNumber ??= index + 1;
                }

                plans.Add(new PlannedImportFile
                {
                    FullPath = item.FullPath,
                    RelativePath = item.RelativePath,
                    SequenceNumber = index + 1,
                    DiskNumberHint = diskNumber,
                    ChapterNumberHint = chapterNumber
                });
            }

            return plans;
        }

        public static Dictionary<string, int> BuildStableNamingNumbers(
            IEnumerable<PlannedImportFile> plans,
            Func<PlannedImportFile, int?> selector)
        {
            var orderedPlans = plans
                .OrderBy(p => p.SequenceNumber)
                .ToList();

            var rawValues = orderedPlans
                .Select(selector)
                .ToList();

            var canUseRawValues = rawValues.Count == orderedPlans.Count
                && rawValues.All(v => v.HasValue)
                && AreStrictlyIncreasing(rawValues.Select(v => v!.Value));

            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < orderedPlans.Count; i++)
            {
                result[orderedPlans[i].FullPath] = canUseRawValues
                    ? rawValues[i]!.Value
                    : orderedPlans[i].SequenceNumber;
            }

            return result;
        }

        /// <summary>
        /// The disc number each file is named with. A disc is only what the source said it was —
        /// a "CD 2" folder, "Disc 3" in the name — carried forward over files that do not say.
        /// With nothing to go on, or numbers that run backwards, everything is disc 1.
        /// </summary>
        /// <param name="plans">The files being imported together, as planned by <see cref="BuildPlans"/>.</param>
        /// <param name="namingPattern">
        /// The pattern the numbers are for. When it has no chapter token the disc number is the
        /// only thing telling the files apart, so it stays one per file as it always was.
        /// </param>
        public static Dictionary<string, int> BuildDiskNumbers(IEnumerable<PlannedImportFile> plans, string? namingPattern)
        {
            if (string.IsNullOrWhiteSpace(namingPattern)
                || namingPattern.IndexOf("ChapterNumber", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return BuildStableNamingNumbers(plans, p => p.DiskNumberHint ?? p.ChapterNumberHint);
            }

            var orderedPlans = plans.OrderBy(p => p.SequenceNumber).ToList();
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var current = 1;
            var trustworthy = orderedPlans.Any(p => p.DiskNumberHint.HasValue);
            foreach (var plan in orderedPlans)
            {
                if (plan.DiskNumberHint is { } hint)
                {
                    trustworthy &= hint >= current || result.Count == 0;
                    current = Math.Max(1, hint);
                }

                result[plan.FullPath] = current;
            }

            if (!trustworthy)
            {
                foreach (var plan in orderedPlans)
                {
                    result[plan.FullPath] = 1;
                }
            }

            return result;
        }

        private static (int? DiskNumber, int? ChapterNumber) InferOrderHints(string? relativePath, string fullPath)
        {
            var pathForHints = GetComparablePath(relativePath, fullPath);
            var fileStem = Path.GetFileNameWithoutExtension(pathForHints);
            var parentDirectory = Path.GetFileName(Path.GetDirectoryName(pathForHints) ?? string.Empty);
            var combined = fileStem;
            if (!string.IsNullOrWhiteSpace(parentDirectory)
                && Regex.IsMatch(parentDirectory, @"\b(?:disc|disk|cd|part|pt|volume|vol|chapter|ch|track)\b", RegexOptions.IgnoreCase))
            {
                combined = parentDirectory + " " + fileStem;
            }

            int? diskNumber = TryParseNumber(DiskPattern.Match(combined));
            int? chapterNumber = TryParseNumber(ChapterPattern.Match(fileStem));

            chapterNumber ??= TryParseNumber(LeadingNumberPattern.Match(fileStem));
            chapterNumber ??= TryParseNumber(TrailingNumberPattern.Match(fileStem));

            if (!chapterNumber.HasValue)
            {
                chapterNumber = TryParseNumber(ChapterPattern.Match(combined));
            }

            return (diskNumber, chapterNumber);
        }

        private static string GetComparablePath(string? relativePath, string fullPath)
        {
            var preferred = string.IsNullOrWhiteSpace(relativePath) ? fullPath : relativePath;
            return preferred.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        }

        private static string ToNaturalSortKey(string value)
        {
            return string.Concat(
                NumericChunkPattern.Matches(value).Select(match =>
                {
                    var chunk = match.Value;
                    return int.TryParse(chunk, out var numeric)
                        ? numeric.ToString("D12")
                        : chunk.ToUpperInvariant();
                }));
        }

        private static int? TryParseNumber(Match match)
        {
            if (!match.Success)
            {
                return null;
            }

            return int.TryParse(match.Groups[1].Value, out var value) ? value : null;
        }

        private static bool AreStrictlyIncreasing(IEnumerable<int> values)
        {
            var hasPrevious = false;
            var previous = 0;

            foreach (var value in values)
            {
                if (hasPrevious && value <= previous)
                {
                    return false;
                }

                previous = value;
                hasPrevious = true;
            }

            return true;
        }
    }
}
