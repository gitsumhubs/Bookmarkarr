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
    /// What the search queue looks like right now, so a waiting user can be shown why nothing has
    /// come back yet instead of a spinner that could mean anything.
    /// </summary>
    /// <param name="MinimumCooldown">
    /// Shortest gap the torrent lane enforces between requests. The real gap is this plus jitter,
    /// so a countdown built from it is a floor and never promises an answer early.
    /// </param>
    /// <param name="CooldownRemaining">
    /// How long before the torrent lane will let the next request out. Zero when nothing is holding
    /// it back.
    /// </param>
    /// <param name="TorrentLaneBusy">
    /// True while a torrent request holds the lane — either waiting out the cooldown or actually
    /// talking to the indexer.
    /// </param>
    /// <param name="TorrentRequestsWaiting">Requests queued behind the one holding the lane.</param>
    /// <param name="BooksWaiting">Whole-book searches queued behind the one in progress.</param>
    public sealed record SearchThrottleSnapshot(
        TimeSpan MinimumCooldown,
        TimeSpan CooldownRemaining,
        bool TorrentLaneBusy,
        int TorrentRequestsWaiting,
        int BooksWaiting);
}
