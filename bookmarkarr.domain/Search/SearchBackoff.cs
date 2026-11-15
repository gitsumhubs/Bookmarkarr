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
    /// How long to leave a book alone after a search that found nothing, and an indexer alone after
    /// it refused.
    /// </summary>
    /// <remarks>
    /// A book that is not on any indexer was searched again every six hours for ever, each time
    /// walking the whole query ladder. A release rarely appears within hours of a miss, so the gap
    /// doubles with every miss up to a week. Searching by hand is never held back by this.
    /// </remarks>
    public static class SearchBackoff
    {
        public static readonly TimeSpan FirstRetry = TimeSpan.FromHours(6);
        public static readonly TimeSpan LongestRetry = TimeSpan.FromDays(7);

        private static readonly TimeSpan[] RateLimitedSteps =
            [TimeSpan.FromHours(1), TimeSpan.FromHours(6), TimeSpan.FromHours(24), TimeSpan.FromHours(72), TimeSpan.FromDays(7)];

        private static readonly TimeSpan[] FailureSteps =
            [TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromHours(6)];

        /// <summary>Wait after the given number of searches in a row that found nothing to take.</summary>
        public static TimeSpan AfterMisses(int misses)
        {
            if (misses <= 1)
            {
                return FirstRetry;
            }

            var hours = FirstRetry.TotalHours * Math.Pow(2, Math.Min(misses - 1, 10));
            return hours >= LongestRetry.TotalHours ? LongestRetry : TimeSpan.FromHours(hours);
        }

        /// <summary>
        /// Wait after an indexer failed the given number of times in a row. A refusal for volume
        /// escalates from an hour to a week, because asking a site that is refusing is what turns a
        /// warning into a ban. Anything else — a timeout, a gateway error — starts at ten minutes.
        /// The indexer's own Retry-After is honoured when it asks for longer.
        /// </summary>
        public static TimeSpan AfterIndexerFailures(int consecutiveFailures, bool rateLimited, TimeSpan? retryAfter)
        {
            var steps = rateLimited ? RateLimitedSteps : FailureSteps;
            var wait = steps[Math.Clamp(consecutiveFailures - 1, 0, steps.Length - 1)];
            return retryAfter.HasValue && retryAfter.Value > wait ? retryAfter.Value : wait;
        }
    }
}
