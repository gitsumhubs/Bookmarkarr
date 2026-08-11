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
    /// Paces searching so a torrent site sees one request at a time, spaced out, rather than a burst.
    /// </summary>
    /// <remarks>
    /// The hourly budget in <see cref="IIndexerQuotaService"/> bounds the total; it does nothing
    /// about shape. Sixty requests may be spent in ninety seconds and still sit inside the budget,
    /// and a burst is exactly what a site notices — AudioBook Bay bans an IP for about a week for
    /// it. This spreads the same requests evenly instead.
    ///
    /// Two lanes, because they answer different questions:
    ///
    /// <list type="bullet">
    /// <item>The book lane holds one whole book's search — every rung of its query ladder, every
    /// indexer — so two books are never in flight together.</item>
    /// <item>The torrent lane holds one outgoing request to one torrent indexer, and arms a
    /// cooldown when it is released, so requests are separated in time as well as serialised.</item>
    /// </list>
    ///
    /// Usenet indexers take neither lane. They are not what bans, and holding them back would mean
    /// a manual search showed nothing at all while a torrent indexer waited its turn.
    ///
    /// Both lanes are process-wide: automatic passes, catalogue imports, retries after a failed
    /// download, and a person clicking Search share one queue.
    /// </remarks>
    public interface ISearchThrottle
    {
        /// <summary>
        /// Shortest the torrent lane stays shut after a request, or after a book that made one.
        /// The real gap is this plus a random slice on top, so requests never arrive on a
        /// metronome — a fixed interval solves bursts and becomes its own signature.
        /// </summary>
        TimeSpan MinimumCooldown { get; }

        /// <summary>
        /// Takes the book lane for the caller's whole search, releasing it on dispose. Releasing
        /// arms the cooldown when the book actually contacted a torrent indexer, so the next book
        /// waits whether this one found something, found nothing, or threw.
        /// </summary>
        /// <param name="description">Book being searched, for the log line that explains a wait.</param>
        /// <param name="torrentIndexerActive">
        /// False when no enabled indexer is a torrent indexer, which passes straight through: a
        /// usenet-only install has nothing to be gentle with and should not be slowed down.
        /// </param>
        Task<IDisposable> AcquireBookAsync(string description, bool torrentIndexerActive, CancellationToken ct = default);

        /// <summary>
        /// Takes the torrent lane for one outgoing request, releasing it on dispose and arming the
        /// cooldown. Acquire immediately before sending, so the wait is not spent holding a
        /// connection open.
        /// </summary>
        Task<IDisposable> AcquireTorrentRequestAsync(string indexerName, CancellationToken ct = default);
    }
}
