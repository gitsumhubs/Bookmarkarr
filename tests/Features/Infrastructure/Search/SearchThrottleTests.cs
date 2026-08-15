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

using Bookmarkarr.Infrastructure.Search.Throttling;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bookmarkarr.Tests.Features.Infrastructure.Search
{
    /// <summary>
    /// The throttle exists to make Bookmarkarr's requests look like a person's rather than a burst,
    /// so what matters is: one book at a time, one torrent request at a time, a real gap between
    /// them, and none of it imposed on an install that searches no torrent site.
    /// </summary>
    /// <remarks>
    /// Run against a cooldown of a fraction of a second rather than the shipped minute. The
    /// behaviour under test is the ordering and the waiting, not the length of the wait.
    /// </remarks>
    public class SearchThrottleTests
    {
        private static readonly TimeSpan TestCooldown = TimeSpan.FromMilliseconds(300);

        /// <summary>
        /// Jitter off unless a test is specifically about it — a random slice on top of the
        /// cooldown would leave the timing assertions with no value to assert.
        /// </summary>
        private static SearchThrottle Build(TimeSpan? cooldown = null, TimeSpan? jitter = null)
            => new(NullLogger<SearchThrottle>.Instance, cooldown ?? TestCooldown, jitter ?? TimeSpan.Zero);

        [Fact]
        public async Task ASecondTorrentRequest_WaitsOutTheCooldown()
        {
            var throttle = Build();

            using (await throttle.AcquireTorrentRequestAsync("AudioBook Bay"))
            {
            }

            var started = DateTime.UtcNow;
            using (await throttle.AcquireTorrentRequestAsync("AudioBook Bay"))
            {
                var waited = DateTime.UtcNow - started;

                // Timer granularity means the wait can land marginally short of the nominal value;
                // what is being asserted is that it waited at all, not stopwatch accuracy.
                Assert.True(
                    waited >= TestCooldown - TimeSpan.FromMilliseconds(50),
                    $"expected a cooldown of about {TestCooldown.TotalMilliseconds}ms, waited {waited.TotalMilliseconds}ms");
            }
        }

        [Fact]
        public async Task TheFirstTorrentRequest_GoesStraightOut()
        {
            var throttle = Build(cooldown: TimeSpan.FromSeconds(30));

            var started = DateTime.UtcNow;
            using var lease = await throttle.AcquireTorrentRequestAsync("AudioBook Bay");

            Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
        }

        [Fact]
        public async Task TorrentRequests_AreNeverInFlightTogether()
        {
            var throttle = Build(cooldown: TimeSpan.Zero);
            var concurrent = 0;
            var peak = 0;

            await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
            {
                using var lease = await throttle.AcquireTorrentRequestAsync("AudioBook Bay");
                peak = Math.Max(peak, Interlocked.Increment(ref concurrent));
                await Task.Delay(10);
                Interlocked.Decrement(ref concurrent);
            }));

            Assert.Equal(1, peak);
        }

        [Fact]
        public async Task ASecondBook_WaitsForTheFirstToFinish()
        {
            var throttle = Build(cooldown: TimeSpan.Zero);
            var order = new List<string>();

            var first = await throttle.AcquireBookAsync("Book A", torrentIndexerActive: true);

            var second = Task.Run(async () =>
            {
                using var lease = await throttle.AcquireBookAsync("Book B", torrentIndexerActive: true);
                lock (order) order.Add("B started");
            });

            await Task.Delay(100);
            lock (order) order.Add("A finished");
            first.Dispose();

            await second;

            Assert.Equal(new[] { "A finished", "B started" }, order);
        }

        [Fact]
        public async Task ABookThatSearchedATorrentIndexer_MakesTheNextBookWait()
        {
            var throttle = Build();

            using (await throttle.AcquireBookAsync("Book A", torrentIndexerActive: true))
            {
                using (await throttle.AcquireTorrentRequestAsync("AudioBook Bay"))
                {
                }
            }

            // Armed at the end of the book, so the gap is measured from there rather than from the
            // request inside it — the point being that the next book waits however this one ended.
            var started = DateTime.UtcNow;
            using (await throttle.AcquireTorrentRequestAsync("AudioBook Bay"))
            {
                Assert.True(DateTime.UtcNow - started >= TestCooldown - TimeSpan.FromMilliseconds(50));
            }
        }

        [Fact]
        public async Task ABookThatSearchedNoTorrentIndexer_ArmsNoCooldown()
        {
            var throttle = Build(cooldown: TimeSpan.FromSeconds(30));

            using (await throttle.AcquireBookAsync("Book A", torrentIndexerActive: true))
            {
                // Usenet only: nothing takes the torrent lane.
            }

            var started = DateTime.UtcNow;
            using var lease = await throttle.AcquireTorrentRequestAsync("AudioBook Bay");

            Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
        }

        [Fact]
        public async Task WithNoTorrentIndexerEnabled_BooksAreNotSerialised()
        {
            var throttle = Build(cooldown: TimeSpan.FromSeconds(30));

            using var first = await throttle.AcquireBookAsync("Book A", torrentIndexerActive: false);

            // Would deadlock against the book lane if a usenet-only install were queued at all.
            var second = throttle.AcquireBookAsync("Book B", torrentIndexerActive: false);
            var completed = await Task.WhenAny(second, Task.Delay(TimeSpan.FromSeconds(5)));

            Assert.Same(second, completed);
            (await second).Dispose();
        }

        /// <summary>
        /// The jitter must never buy back speed. Sixty seconds is the promise; the randomness sits
        /// on top of it, so the shortest possible gap is still the full cooldown.
        /// </summary>
        [Fact]
        public async Task Jitter_NeverShortensTheCooldown()
        {
            var cooldown = TimeSpan.FromMilliseconds(200);
            var throttle = Build(cooldown: cooldown, jitter: TimeSpan.FromMilliseconds(200));

            for (var i = 0; i < 8; i++)
            {
                using (await throttle.AcquireTorrentRequestAsync("AudioBook Bay"))
                {
                }

                var started = DateTime.UtcNow;
                using (await throttle.AcquireTorrentRequestAsync("AudioBook Bay"))
                {
                    var waited = DateTime.UtcNow - started;
                    Assert.True(
                        waited >= cooldown - TimeSpan.FromMilliseconds(50),
                        $"gap of {waited.TotalMilliseconds}ms fell below the {cooldown.TotalMilliseconds}ms floor");
                }
            }
        }

        /// <summary>
        /// Requests arriving exactly the same distance apart every time are their own signature, so
        /// the gaps have to actually differ rather than merely be drawn from a range.
        /// </summary>
        [Fact]
        public async Task Jitter_MakesTheGapsDiffer()
        {
            var throttle = Build(cooldown: TimeSpan.Zero, jitter: TimeSpan.FromMilliseconds(400));
            var gaps = new List<double>();

            for (var i = 0; i < 6; i++)
            {
                using (await throttle.AcquireTorrentRequestAsync("AudioBook Bay"))
                {
                }

                var started = DateTime.UtcNow;
                using (await throttle.AcquireTorrentRequestAsync("AudioBook Bay"))
                {
                    gaps.Add((DateTime.UtcNow - started).TotalMilliseconds);
                }
            }

            // Rounded to 50ms buckets so timer noise alone cannot pass this — the spread has to be
            // real, not a millisecond of scheduling variance.
            Assert.True(
                gaps.Select(gap => Math.Round(gap / 50)).Distinct().Count() > 1,
                $"every gap landed in the same bucket: {string.Join(", ", gaps)}");
        }

        /// <summary>The shipped values, which are what the site actually experiences.</summary>
        [Fact]
        public void TheShippedCooldown_IsAMinuteWithRoomAbove()
        {
            Assert.Equal(TimeSpan.FromSeconds(60), SearchThrottle.TorrentCooldown);
            Assert.True(SearchThrottle.TorrentCooldownJitter > TimeSpan.Zero);
        }

        [Fact]
        public async Task GivingUpDuringTheCooldown_LeavesTheLaneUsable()
        {
            var throttle = Build();

            using (await throttle.AcquireTorrentRequestAsync("AudioBook Bay"))
            {
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => throttle.AcquireTorrentRequestAsync("AudioBook Bay", cts.Token));

            // The abandoned wait must not have left the lane shut behind it. Releasing on the way
            // out is the whole reason this passes rather than hanging until the test times out.
            var next = throttle.AcquireTorrentRequestAsync("AudioBook Bay");
            var completed = await Task.WhenAny(next, Task.Delay(TimeSpan.FromSeconds(10)));

            Assert.Same(next, completed);
            (await next).Dispose();
        }

        /// <summary>
        /// The snapshot is what a waiting user is shown instead of an open-ended spinner, so it has
        /// to report the wait that is actually happening rather than a nominal one.
        /// </summary>
        [Fact]
        public async Task TheSnapshot_ReportsTheCooldownAWaitingRequestWillActuallyServe()
        {
            var throttle = Build(cooldown: TimeSpan.FromSeconds(30));

            Assert.Equal(TimeSpan.Zero, throttle.GetSnapshot().CooldownRemaining);

            using (await throttle.AcquireTorrentRequestAsync("AudioBook Bay"))
            {
            }

            var snapshot = throttle.GetSnapshot();

            Assert.Equal(TimeSpan.FromSeconds(30), snapshot.MinimumCooldown);
            Assert.True(
                snapshot.CooldownRemaining > TimeSpan.Zero
                    && snapshot.CooldownRemaining <= TimeSpan.FromSeconds(30),
                $"expected a cooldown inside 30s, got {snapshot.CooldownRemaining}");
            Assert.False(snapshot.TorrentLaneBusy);
            Assert.Equal(0, snapshot.TorrentRequestsWaiting);
            Assert.Equal(0, snapshot.BooksWaiting);
        }

        /// <summary>
        /// A queue depth of zero while somebody is stuck behind a lane would be the one reading that
        /// makes the wait look inexplicable, which is the opposite of the point.
        /// </summary>
        [Fact]
        public async Task TheSnapshot_CountsWhoIsQueuedBehindEachLane()
        {
            var throttle = Build(cooldown: TimeSpan.Zero);

            using var book = await throttle.AcquireBookAsync("Book A", torrentIndexerActive: true);
            using var request = await throttle.AcquireTorrentRequestAsync("AudioBook Bay");

            var queuedBook = Task.Run(() => throttle.AcquireBookAsync("Book B", torrentIndexerActive: true));
            var queuedRequest = Task.Run(() => throttle.AcquireTorrentRequestAsync("AudioBook Bay"));

            SearchThrottleSnapshot snapshot;
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            do
            {
                await Task.Delay(20);
                snapshot = throttle.GetSnapshot();
            }
            while ((snapshot.BooksWaiting == 0 || snapshot.TorrentRequestsWaiting == 0)
                   && DateTime.UtcNow < deadline);

            Assert.Equal(1, snapshot.BooksWaiting);
            Assert.Equal(1, snapshot.TorrentRequestsWaiting);
            Assert.True(snapshot.TorrentLaneBusy);

            request.Dispose();
            book.Dispose();
            (await queuedBook).Dispose();
            (await queuedRequest).Dispose();
        }
    }
}
