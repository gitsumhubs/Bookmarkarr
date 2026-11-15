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

using Bookmarkarr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bookmarkarr.Infrastructure.Search.Quota
{
    /// <summary>
    /// Enforces a rolling hourly request budget per indexer.
    /// </summary>
    /// <remarks>
    /// The window rolls: a request taken at 09:14 frees up at 10:14, so the countdown shown to an
    /// operator is the real moment capacity returns rather than the top of some hour. Consumption
    /// is serialised in-process, because two searches starting together would otherwise both read
    /// the same count and both decide there was room.
    /// </remarks>
    public sealed class IndexerQuotaService : IIndexerQuotaService
    {
        /// <summary>Length of the rolling window. Hourly, matching how indexers express limits.</summary>
        private static readonly TimeSpan Window = TimeSpan.FromHours(1);

        /// <summary>Rows are kept a little past the window so a countdown never reads a pruned row.</summary>
        private static readonly TimeSpan Retention = TimeSpan.FromHours(2);

        /// <summary>
        /// Share of the budget automation may not touch, so a person who clicks Search is not
        /// starved by an unattended pass that has been running all hour.
        /// </summary>
        private const double InteractiveReserveFraction = 0.2;

        private static readonly SemaphoreSlim ConsumeLock = new(1, 1);

        private readonly BookmarkarrDbContext _db;
        private readonly ILogger<IndexerQuotaService> _logger;

        public IndexerQuotaService(BookmarkarrDbContext db, ILogger<IndexerQuotaService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<IndexerQuotaState> GetStateAsync(int indexerId, CancellationToken ct = default)
        {
            var indexer = await _db.Indexers.AsNoTracking().FirstOrDefaultAsync(i => i.Id == indexerId, ct);
            if (indexer == null)
            {
                return IndexerQuotaState.Unlimited(indexerId, $"Indexer {indexerId}");
            }

            // An AudioBook Bay indexer with nothing set still gets a budget: the setting was only
            // ever filled in by the in-app patch, and a hand-installed definition left it empty.
            var limit = AudiobookBayPolicy.EffectiveRequestsPerHour(indexer);
            if (!limit.HasValue || limit.Value <= 0)
            {
                return IndexerQuotaState.Unlimited(indexerId, indexer.Name);
            }

            var occurrences = await WindowOccurrencesAsync(indexerId, ct);
            return Describe(indexerId, indexer.Name, limit.Value, occurrences);
        }

        public async Task<bool> TryConsumeAsync(int indexerId, IndexerQuotaPurpose purpose, CancellationToken ct = default)
        {
            await ConsumeLock.WaitAsync(ct);
            try
            {
                if (await GetCooldownAsync(indexerId, ct) != null)
                {
                    return false;
                }

                var state = await GetStateAsync(indexerId, ct);
                var cost = await CostAsync(indexerId, purpose, state, ct);
                if (state.IsLimited && !CanSpend(state, purpose, cost))
                {
                    _logger.LogInformation(
                        "Indexer {Name} budget spent for {Purpose}: {Used}/{Limit} used this hour, {Cost} needed",
                        state.IndexerName,
                        purpose,
                        state.Used,
                        state.RequestsPerHour,
                        cost);
                    return false;
                }

                if (state.IsLimited)
                {
                    // One row per request the site will see, so a three-page search fills the
                    // window three times as fast as a grab does.
                    var now = DateTime.UtcNow;
                    for (var i = 0; i < cost; i++)
                    {
                        _db.IndexerQuotaUsages.Add(new IndexerQuotaUsage { IndexerId = indexerId, OccurredAtUtc = now, Purpose = purpose });
                    }

                    await _db.SaveChangesAsync(ct);
                }

                return true;
            }
            finally
            {
                ConsumeLock.Release();
            }
        }

        public async Task<TimeSpan?> TimeUntilAvailableAsync(int indexerId, IndexerQuotaPurpose purpose, CancellationToken ct = default)
        {
            var cooldown = await GetCooldownAsync(indexerId, ct);
            var cooldownWait = cooldown == null ? (TimeSpan?)null : cooldown.UntilUtc - DateTime.UtcNow;

            var state = await GetStateAsync(indexerId, ct);
            var cost = await CostAsync(indexerId, purpose, state, ct);
            if (!state.IsLimited || CanSpend(state, purpose, cost))
            {
                return cooldownWait;
            }

            var occurrences = await WindowOccurrencesAsync(indexerId, ct);
            var freeAt = NextSlotFor(state, purpose, occurrences, cost);
            if (freeAt == null)
            {
                return cooldownWait;
            }

            var wait = freeAt.Value - DateTime.UtcNow;
            wait = wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
            return cooldownWait.HasValue && cooldownWait.Value > wait ? cooldownWait : wait;
        }

        public async Task<IndexerCooldown?> GetCooldownAsync(int indexerId, CancellationToken ct = default)
        {
            var indexer = await _db.Indexers.AsNoTracking()
                .Where(i => i.Id == indexerId)
                .Select(i => new { i.Name, i.UnavailableUntilUtc, i.ConsecutiveFailures, i.LastFailureReason })
                .FirstOrDefaultAsync(ct);
            return indexer?.UnavailableUntilUtc is { } until && until > DateTime.UtcNow
                ? new IndexerCooldown(indexer.Name, until, indexer.ConsecutiveFailures, indexer.LastFailureReason)
                : null;
        }

        public async Task<DateTime> RecordFailureAsync(int indexerId, bool rateLimited, TimeSpan? retryAfter, string reason, CancellationToken ct = default)
        {
            var indexer = await _db.Indexers.FirstOrDefaultAsync(i => i.Id == indexerId, ct);
            if (indexer == null)
            {
                return DateTime.UtcNow;
            }

            indexer.ConsecutiveFailures++;
            var wait = SearchBackoff.AfterIndexerFailures(indexer.ConsecutiveFailures, rateLimited, retryAfter);
            indexer.UnavailableUntilUtc = DateTime.UtcNow + wait;
            indexer.LastFailureReason = reason.Length > 300 ? reason[..300] : reason;
            await _db.SaveChangesAsync(ct);
            _logger.LogWarning(
                "Indexer {Name} failed {Count} time(s) in a row; leaving it alone for {Wait}",
                indexer.Name,
                indexer.ConsecutiveFailures,
                wait);
            return indexer.UnavailableUntilUtc.Value;
        }

        public async Task RecordSuccessAsync(int indexerId, CancellationToken ct = default)
        {
            var indexer = await _db.Indexers.FirstOrDefaultAsync(i => i.Id == indexerId, ct);
            if (indexer == null || (indexer.ConsecutiveFailures == 0 && indexer.UnavailableUntilUtc == null))
            {
                return;
            }

            indexer.ConsecutiveFailures = 0;
            indexer.UnavailableUntilUtc = null;
            indexer.LastFailureReason = null;
            await _db.SaveChangesAsync(ct);
        }

        /// <summary>
        /// Site requests this spend stands for. A grab is one. A search is as many pages as the
        /// indexer's definition reads, capped at what the purpose could ever spend so a budget
        /// smaller than one search does not shut the indexer for good.
        /// </summary>
        private async Task<int> CostAsync(int indexerId, IndexerQuotaPurpose purpose, IndexerQuotaState state, CancellationToken ct)
        {
            if (purpose == IndexerQuotaPurpose.Grab || !state.IsLimited)
            {
                return 1;
            }

            var indexer = await _db.Indexers.AsNoTracking().FirstOrDefaultAsync(i => i.Id == indexerId, ct);
            if (indexer == null)
            {
                return 1;
            }

            var patch = await _db.ApplicationSettings.AsNoTracking()
                .Select(settings => new { settings.AudiobookBayPatchIndexerId, settings.AudiobookBayPatchPages })
                .FirstOrDefaultAsync(ct);
            var pages = AudiobookBayPolicy.RequestsPerSearch(indexer, patch?.AudiobookBayPatchIndexerId, patch?.AudiobookBayPatchPages);
            var ceiling = purpose == IndexerQuotaPurpose.AutomaticSearch
                ? state.RequestsPerHour!.Value - state.InteractiveReserve
                : state.RequestsPerHour!.Value;
            return Math.Clamp(pages, 1, Math.Max(1, ceiling));
        }

        public async Task PruneAsync(CancellationToken ct = default)
        {
            var cutoff = DateTime.UtcNow - Retention;
            var removed = await _db.IndexerQuotaUsages
                .Where(u => u.OccurredAtUtc < cutoff)
                .ExecuteDeleteAsync(ct);

            if (removed > 0)
            {
                _logger.LogDebug("Pruned {Count} expired indexer quota rows", removed);
            }
        }

        private async Task<List<DateTime>> WindowOccurrencesAsync(int indexerId, CancellationToken ct)
        {
            var windowStart = DateTime.UtcNow - Window;
            return await _db.IndexerQuotaUsages
                .AsNoTracking()
                .Where(u => u.IndexerId == indexerId && u.OccurredAtUtc >= windowStart)
                .OrderBy(u => u.OccurredAtUtc)
                .Select(u => u.OccurredAtUtc)
                .ToListAsync(ct);
        }

        private static IndexerQuotaState Describe(int indexerId, string name, int limit, List<DateTime> occurrences)
        {
            var used = occurrences.Count;
            var remaining = Math.Max(0, limit - used);
            var reserve = ReserveFor(limit);
            var automaticRemaining = Math.Max(0, remaining - reserve);

            var state = new IndexerQuotaState(
                indexerId,
                name,
                limit,
                used,
                remaining,
                reserve,
                automaticRemaining,
                null,
                occurrences.Count > 0 ? occurrences[^1] + Window : null);

            // Reported against the stricter of the two, so a caller with nothing to spend still
            // sees a countdown rather than an empty field.
            var purpose = automaticRemaining > 0 ? IndexerQuotaPurpose.InteractiveSearch : IndexerQuotaPurpose.AutomaticSearch;
            return state with { NextSlotUtc = NextSlotFor(state, purpose, occurrences) };
        }

        /// <summary>
        /// Automation stops at the reserve; a person, and a grab they are waiting on, may spend to
        /// the last request. Beyond that nothing is sent, since one more request is exactly what
        /// re-arms a ban.
        /// </summary>
        private static bool CanSpend(IndexerQuotaState state, IndexerQuotaPurpose purpose, int cost = 1)
            => purpose == IndexerQuotaPurpose.AutomaticSearch
                ? state.AutomaticRemaining >= cost
                : state.Remaining >= cost;

        private static int ReserveFor(int limit)
        {
            if (limit <= 2)
            {
                return 0;
            }

            var reserve = (int)Math.Ceiling(limit * InteractiveReserveFraction);
            return Math.Clamp(reserve, 1, limit - 1);
        }

        /// <summary>
        /// When enough of the window has aged out for <paramref name="purpose"/> to spend again.
        /// </summary>
        private static DateTime? NextSlotFor(IndexerQuotaState state, IndexerQuotaPurpose purpose, List<DateTime> occurrences, int cost = 1)
        {
            if (occurrences.Count == 0 || !state.RequestsPerHour.HasValue)
            {
                return null;
            }

            var ceiling = purpose == IndexerQuotaPurpose.AutomaticSearch
                ? state.RequestsPerHour.Value - state.InteractiveReserve
                : state.RequestsPerHour.Value;

            // Requests that have to expire before one more fits under the ceiling.
            var mustExpire = state.Used - ceiling + cost;
            if (mustExpire <= 0)
            {
                return null;
            }

            var index = Math.Min(mustExpire, occurrences.Count) - 1;
            return occurrences[index] + Window;
        }
    }
}
