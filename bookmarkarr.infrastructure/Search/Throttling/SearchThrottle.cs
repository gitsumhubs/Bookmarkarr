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

using Microsoft.Extensions.Logging;

namespace Bookmarkarr.Infrastructure.Search.Throttling
{
    /// <summary>
    /// Process-wide pacing for searches: one book at a time, one torrent request at a time, and a
    /// cooldown between them.
    /// </summary>
    /// <remarks>
    /// Registered as a singleton — two instances would be two queues, which is no queue at all.
    /// </remarks>
    public sealed class SearchThrottle : ISearchThrottle
    {
        /// <summary>
        /// Quiet period after a torrent request, and after a book that made one, before the next
        /// request goes out.
        /// </summary>
        /// <remarks>
        /// A minute is deliberately slower than the hourly budget requires. It is the shape that
        /// matters: sixty seconds apart is roughly how often a person refreshes a search page, and
        /// it caps the site at sixty requests an hour no matter how many books are waiting.
        ///
        /// This is a floor, never an average — see <see cref="TorrentCooldownJitter"/>.
        /// </remarks>
        public static readonly TimeSpan TorrentCooldown = TimeSpan.FromSeconds(60);

        /// <summary>
        /// Added to <see cref="TorrentCooldown"/> at random, so the real gap falls somewhere in
        /// 60-90 seconds rather than landing on the same value every time.
        /// </summary>
        /// <remarks>
        /// A fixed interval solves bursts and replaces them with a metronome, and requests arriving
        /// exactly 60.000s apart are their own signature — no person produces that. The spread is
        /// deliberately one-sided: the minimum is the guarantee, and drawing around a mean would
        /// put half the requests inside it.
        /// </remarks>
        public static readonly TimeSpan TorrentCooldownJitter = TimeSpan.FromSeconds(30);

        /// <summary>Waits longer than this are announced, so a search that looks hung explains itself.</summary>
        private static readonly TimeSpan NotableWait = TimeSpan.FromSeconds(1);

        private readonly SemaphoreSlim _bookLane = new(1, 1);
        private readonly SemaphoreSlim _torrentLane = new(1, 1);
        private readonly ILogger<SearchThrottle> _logger;

        /// <summary>Guards <see cref="_nextTorrentRequestUtc"/>, which is read and written off-lane.</summary>
        private readonly Lock _cooldownGate = new();

        private DateTime _nextTorrentRequestUtc = DateTime.MinValue;

        /// <summary>
        /// Counts torrent requests so a book lease can tell whether it contacted a torrent indexer
        /// at all. A book that only searched usenet arms no cooldown.
        /// </summary>
        private long _torrentRequests;

        /// <summary>
        /// Callers blocked on each lane, counted only so <see cref="GetSnapshot"/> can say how deep
        /// the queue is. Incremented before the wait and decremented however the wait ends, so a
        /// cancelled caller does not leave itself in the count forever.
        /// </summary>
        private int _booksWaiting;
        private int _torrentRequestsWaiting;

        private readonly TimeSpan _cooldown;
        private readonly TimeSpan _jitter;

        public SearchThrottle(ILogger<SearchThrottle> logger)
            : this(logger, TorrentCooldown, TorrentCooldownJitter)
        {
        }

        /// <summary>
        /// Test seam: the same throttle with a cooldown a test can afford to wait out, and jitter a
        /// test can switch off so a timing assertion has a value to assert.
        /// </summary>
        internal SearchThrottle(ILogger<SearchThrottle> logger, TimeSpan cooldown, TimeSpan jitter)
        {
            _logger = logger;
            _cooldown = cooldown;
            _jitter = jitter;
        }

        public TimeSpan MinimumCooldown => _cooldown;

        public SearchThrottleSnapshot GetSnapshot()
        {
            DateTime nextRequestUtc;
            lock (_cooldownGate)
            {
                nextRequestUtc = _nextTorrentRequestUtc;
            }

            var remaining = nextRequestUtc - DateTime.UtcNow;
            if (remaining < TimeSpan.Zero)
            {
                remaining = TimeSpan.Zero;
            }

            return new SearchThrottleSnapshot(
                _cooldown,
                remaining,
                // A semaphore with no permits left is one somebody is holding. That covers both
                // halves of a torrent request's turn: waiting out the cooldown and the request.
                _torrentLane.CurrentCount == 0,
                Volatile.Read(ref _torrentRequestsWaiting),
                Volatile.Read(ref _booksWaiting));
        }

        public async Task<IDisposable> AcquireBookAsync(string description, bool torrentIndexerActive, CancellationToken ct = default)
        {
            if (!torrentIndexerActive)
            {
                return NullLease.Instance;
            }

            var queuedAt = DateTime.UtcNow;
            Interlocked.Increment(ref _booksWaiting);
            try
            {
                await _bookLane.WaitAsync(ct);
            }
            finally
            {
                Interlocked.Decrement(ref _booksWaiting);
            }

            var queuedFor = DateTime.UtcNow - queuedAt;
            if (queuedFor > NotableWait)
            {
                _logger.LogInformation(
                    "Search queue: '{Description}' waited {Seconds:F0}s for the book ahead of it",
                    description,
                    queuedFor.TotalSeconds);
            }

            var requestsAtStart = Interlocked.Read(ref _torrentRequests);
            return new Lease(() =>
            {
                // Measured from the end of the book rather than the end of its last request, so the
                // gap is the one that was asked for even when scoring and grabbing ran long. A book
                // that threw releases here too — a failure is not a reason to hurry the next one.
                if (Interlocked.Read(ref _torrentRequests) != requestsAtStart)
                {
                    var cooldown = ArmCooldown();
                    _logger.LogInformation(
                        "Search queue: '{Description}' finished; next book waits {Seconds:F0}s",
                        description,
                        cooldown.TotalSeconds);
                }

                _bookLane.Release();
            });
        }

        public async Task<IDisposable> AcquireTorrentRequestAsync(string indexerName, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _torrentRequestsWaiting);
            try
            {
                await _torrentLane.WaitAsync(ct);
            }
            finally
            {
                Interlocked.Decrement(ref _torrentRequestsWaiting);
            }

            try
            {
                await WaitOutCooldownAsync(indexerName, ct);
            }
            catch
            {
                // A caller that gave up waiting must not leave the lane shut behind it.
                _torrentLane.Release();
                throw;
            }

            Interlocked.Increment(ref _torrentRequests);
            return new Lease(() =>
            {
                ArmCooldown();
                _torrentLane.Release();
            });
        }

        private async Task WaitOutCooldownAsync(string indexerName, CancellationToken ct)
        {
            TimeSpan remaining;
            lock (_cooldownGate)
            {
                remaining = _nextTorrentRequestUtc - DateTime.UtcNow;
            }

            if (remaining <= TimeSpan.Zero)
            {
                return;
            }

            if (remaining > NotableWait)
            {
                _logger.LogInformation(
                    "Holding the {Indexer} search {Seconds:F0}s to keep requests at least {Cooldown:F0}s apart",
                    indexerName,
                    remaining.TotalSeconds,
                    _cooldown.TotalSeconds);
            }

            await Task.Delay(remaining, ct);
        }

        /// <summary>
        /// Shuts the torrent lane for the minimum plus a random slice of the jitter, and reports
        /// the gap it chose.
        /// </summary>
        private TimeSpan ArmCooldown()
        {
            var cooldown = _jitter > TimeSpan.Zero
                ? _cooldown + Random.Shared.NextDouble() * _jitter
                : _cooldown;

            lock (_cooldownGate)
            {
                _nextTorrentRequestUtc = DateTime.UtcNow + cooldown;
            }

            return cooldown;
        }

        private sealed class Lease(Action release) : IDisposable
        {
            private Action? _release = release;

            public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
        }

        private sealed class NullLease : IDisposable
        {
            public static readonly NullLease Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
