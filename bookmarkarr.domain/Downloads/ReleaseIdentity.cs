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

namespace Bookmarkarr.Domain.Downloads
{
    /// <summary>
    /// Identifies one specific release across a grab, a failure, and a later search.
    ///
    /// The indexer GUID is the only trustworthy identity, so it wins whenever both sides
    /// carry one. Plenty of indexers omit it or rotate it between queries, hence the
    /// title-plus-size fallback.
    /// </summary>
    public static class ReleaseIdentity
    {
        /// <summary>
        /// Reported sizes drift slightly between an indexer's search response and its
        /// download entry (rounding, padding, par2 accounting). One percent absorbs that
        /// without letting a genuinely different release of the same book slip through.
        /// </summary>
        private const double SizeTolerance = 0.01;

        private static readonly Regex SeparatorPattern = new(@"[\-_\.\s]+", RegexOptions.Compiled);

        /// <summary>
        /// Normalizes a release name for comparison.
        ///
        /// Deliberately NOT <see cref="Common.TitleUtils.NormalizeTitle"/>: that strips
        /// quality, format and group tags, which is exactly what distinguishes one release
        /// of a book from another. Blocklisting on a title-normalized name would block
        /// every release of the book after a single bad grab. Only separators and casing
        /// are flattened here.
        /// </summary>
        public static string NormalizeReleaseName(string? releaseName)
        {
            if (string.IsNullOrWhiteSpace(releaseName))
            {
                return string.Empty;
            }

            return SeparatorPattern.Replace(releaseName, " ").Trim().ToLowerInvariant();
        }

        /// <summary>
        /// True when both sides describe the same release.
        /// </summary>
        /// <remarks>
        /// Indexer ids are compared only when both sides supply one. A missing indexer id must not
        /// veto a GUID match, or legacy rows recorded before the id was persisted stop matching.
        /// </remarks>
        public static bool Matches(
            string? knownGuid,
            int? knownIndexerId,
            string? knownNormalizedTitle,
            long knownSize,
            string? candidateGuid,
            int? candidateIndexerId,
            string? candidateTitle,
            long candidateSize)
        {
            if (knownIndexerId.HasValue && candidateIndexerId.HasValue && knownIndexerId != candidateIndexerId)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(knownGuid) && !string.IsNullOrWhiteSpace(candidateGuid))
            {
                return string.Equals(knownGuid, candidateGuid, StringComparison.OrdinalIgnoreCase);
            }

            if (string.IsNullOrWhiteSpace(knownNormalizedTitle))
            {
                return false;
            }

            var candidateNormalized = NormalizeReleaseName(candidateTitle);
            if (!string.Equals(knownNormalizedTitle, candidateNormalized, StringComparison.Ordinal))
            {
                return false;
            }

            return SizesMatch(knownSize, candidateSize);
        }

        /// <summary>
        /// An unknown size on either side cannot disprove a title match, so it is treated
        /// as agreement rather than silently letting a known-bad release back through.
        /// </summary>
        private static bool SizesMatch(long knownSize, long candidateSize)
        {
            if (knownSize <= 0 || candidateSize <= 0)
            {
                return true;
            }

            var largest = Math.Max(knownSize, candidateSize);
            return Math.Abs(knownSize - candidateSize) <= largest * SizeTolerance;
        }
    }
}
