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
using Microsoft.EntityFrameworkCore;

namespace Bookmarkarr.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// Queries that drive automatic search, kept apart from general audiobook persistence.
    /// </summary>
    public partial class AudiobookRepository
    {
        /// <summary>
        /// Monitored books due a search, longest-waiting first.
        /// </summary>
        /// <remarks>
        /// The order used to be whatever SQLite returned, which was stable — fine while every pass
        /// searched the whole list, and starvation the moment one cannot. A pass that stops early
        /// on a spent request budget would otherwise re-search the same head of the list forever
        /// and never reach the tail. Oldest first makes each pass resume where the last stopped,
        /// with never-searched books ahead of everything.
        /// </remarks>
        public async Task<List<Audiobook>> GetMonitoredAudiobooksForSearchAsync(DateTime cutoff, System.Threading.CancellationToken ct = default)
        {
            return await _db.Audiobooks
                .Include(a => a.QualityProfile)
                .Where(a => a.Monitored &&
                            a.QualityProfileId != null &&
                            (a.LastSearchTime == null || a.LastSearchTime < cutoff))
                .OrderBy(a => a.LastSearchTime == null ? 0 : 1)
                .ThenBy(a => a.LastSearchTime)
                .ThenBy(a => a.Id)
                .ToListAsync(ct);
        }
    }
}
