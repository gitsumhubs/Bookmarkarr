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

namespace Bookmarkarr.Domain.Search
{
    /// <summary>
    /// What Bookmarkarr assumes about an AudioBook Bay indexer when nobody has told it otherwise.
    /// </summary>
    /// <remarks>
    /// The site bans an IP for about a week when asked too often. The hourly budget used to be set
    /// only when the in-app patch was applied, so an indexer pointed at a hand-installed patched
    /// definition had no budget at all. And the budget counted searches, while the site counts
    /// page requests: a patched search reads several pages, so sixty "requests" were up to 180.
    /// </remarks>
    public static class AudiobookBayPolicy
    {
        /// <summary>Site page requests allowed in a rolling hour when the indexer sets no budget.</summary>
        public const int DefaultRequestsPerHour = 60;

        /// <summary>Pages one search reads when the patch is in use and its page count is not recorded.</summary>
        public const int DefaultPagesPerSearch = 3;

        public static bool IsAudiobookBay(Indexer indexer) =>
            Mentions(indexer.Name) || Mentions(indexer.Url);

        /// <summary>
        /// The indexer's own budget when it has one. Zero stays "unlimited", because somebody typed
        /// it; only an empty setting on an AudioBook Bay indexer is filled in.
        /// </summary>
        public static int? EffectiveRequestsPerHour(Indexer indexer) =>
            indexer.RequestsPerHour ?? (IsAudiobookBay(indexer) ? DefaultRequestsPerHour : null);

        /// <summary>
        /// Site requests one search costs. The recorded page count for the patched indexer; for any
        /// other AudioBook Bay indexer the patch's default, since assuming one page when it reads
        /// three is the mistake that leads to a ban.
        /// </summary>
        public static int RequestsPerSearch(Indexer indexer, int? patchedIndexerId, int? patchedPages)
        {
            if (patchedIndexerId == indexer.Id && patchedPages is > 0)
            {
                return patchedPages.Value;
            }

            return IsAudiobookBay(indexer) ? DefaultPagesPerSearch : 1;
        }

        private static bool Mentions(string? value) =>
            !string.IsNullOrWhiteSpace(value)
            && new string(value.Where(char.IsLetterOrDigit).ToArray()).Contains("audiobookbay", StringComparison.OrdinalIgnoreCase);
    }
}
