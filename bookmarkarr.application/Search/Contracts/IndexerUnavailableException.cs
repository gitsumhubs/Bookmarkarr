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

namespace Bookmarkarr.Application.Search.Contracts
{
    /// <summary>
    /// An indexer could not answer. Distinct from answering with nothing.
    /// </summary>
    /// <remarks>
    /// These two used to be the same value. A provider turned any non-success response into an
    /// empty result list, so a 429, a timeout, or a gateway error was indistinguishable from "this
    /// book is not on the site" — which then drove the search ladder to try every remaining rung
    /// against an indexer that was already refusing, and let a throttled book read as unfindable.
    /// Raising instead keeps "could not ask" and "asked, nothing there" apart.
    /// </remarks>
    public class IndexerUnavailableException : Exception
    {
        public IndexerUnavailableException(
            string indexerName,
            string message,
            bool isRateLimited = false,
            TimeSpan? retryAfter = null,
            Exception? innerException = null)
            : base(message, innerException)
        {
            IndexerName = indexerName;
            IsRateLimited = isRateLimited;
            RetryAfter = retryAfter;
        }

        public string IndexerName { get; }

        /// <summary>The indexer refused because of request volume, ours or the budget's.</summary>
        public bool IsRateLimited { get; }

        /// <summary>How long to wait before asking again, when that is known.</summary>
        public TimeSpan? RetryAfter { get; }

        /// <summary>The local budget refused before any request left the process.</summary>
        public static IndexerUnavailableException QuotaExhausted(string indexerName, TimeSpan? retryAfter)
            => new(
                indexerName,
                retryAfter.HasValue
                    ? $"{indexerName} has used its hourly request budget. Next request in {FormatWait(retryAfter.Value)}."
                    : $"{indexerName} has used its hourly request budget.",
                isRateLimited: true,
                retryAfter: retryAfter);

        private static string FormatWait(TimeSpan wait)
            => wait.TotalMinutes >= 1
                ? $"{(int)Math.Ceiling(wait.TotalMinutes)} min"
                : $"{Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds))} s";
    }
}
