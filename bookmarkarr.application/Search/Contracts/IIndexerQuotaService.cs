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
    /// A rolling hourly request budget per indexer, covering searches and grabs.
    /// </summary>
    /// <remarks>
    /// Some indexers ban an IP for requesting too often — AudioBook Bay bans for about a week, and
    /// the pagination patch multiplies every query by its page count. A budget here bounds the
    /// worst case regardless of how enthusiastically the rest of the application searches.
    ///
    /// The window rolls rather than resetting on the hour, so the answer to "when can I search
    /// again" is the moment the oldest request ages out, not an arbitrary boundary that invites
    /// everything to fire at once.
    /// </remarks>
    public interface IIndexerQuotaService
    {
        /// <summary>Current budget, usage, and the moment the next request becomes possible.</summary>
        Task<IndexerQuotaState> GetStateAsync(int indexerId, CancellationToken ct = default);

        /// <summary>
        /// Spend one request if the budget allows, recording it. False means the caller must not
        /// contact the indexer — the whole point is that a refused request is never sent.
        /// </summary>
        Task<bool> TryConsumeAsync(int indexerId, IndexerQuotaPurpose purpose, CancellationToken ct = default);

        /// <summary>
        /// How long until <paramref name="purpose"/> could spend a request, or null when it can now.
        /// </summary>
        Task<TimeSpan?> TimeUntilAvailableAsync(int indexerId, IndexerQuotaPurpose purpose, CancellationToken ct = default);

        /// <summary>Drop usage rows that have aged out of every window.</summary>
        Task PruneAsync(CancellationToken ct = default);
    }

    /// <summary>A point-in-time view of one indexer's budget, shaped for display.</summary>
    /// <param name="IndexerId">Indexer the budget belongs to.</param>
    /// <param name="IndexerName">Indexer's display name.</param>
    /// <param name="RequestsPerHour">The budget, or null when the indexer is unlimited.</param>
    /// <param name="Used">Requests inside the current rolling hour.</param>
    /// <param name="Remaining">What anything may still spend.</param>
    /// <param name="InteractiveReserve">Held back so a person's search is not starved by automation.</param>
    /// <param name="AutomaticRemaining">What unattended search may still spend.</param>
    /// <param name="NextSlotUtc">When the next request frees up, if the budget is spent.</param>
    /// <param name="WindowEndsUtc">When the window empties completely.</param>
    public sealed record IndexerQuotaState(
        int IndexerId,
        string IndexerName,
        int? RequestsPerHour,
        int Used,
        int Remaining,
        int InteractiveReserve,
        int AutomaticRemaining,
        DateTime? NextSlotUtc,
        DateTime? WindowEndsUtc)
    {
        /// <summary>An unlimited indexer never blocks and never reports a countdown.</summary>
        public bool IsLimited => RequestsPerHour.HasValue && RequestsPerHour.Value > 0;

        public static IndexerQuotaState Unlimited(int indexerId, string indexerName)
            => new(indexerId, indexerName, null, 0, int.MaxValue, 0, int.MaxValue, null, null);
    }
}
