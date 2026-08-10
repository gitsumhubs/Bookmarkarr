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

        /// <summary>
        /// Stops monitoring editions that are imported and hold files, and the books whose editions
        /// are all satisfied. Returns how many editions were retired.
        /// </summary>
        /// <remarks>
        /// Monitoring lives at two levels and only one of them is visible: the settings UI shows an
        /// edition's monitor, while automatic search selects on the book's. Retiring the edition
        /// alone would leave the book eligible and the searches running, so both move together —
        /// the book once every one of its editions has stopped asking for anything.
        ///
        /// Only editions with a file are touched. A wanted edition, a failed import, and anything
        /// still downloading keep their monitor, which is what makes this safe to run every pass
        /// rather than once.
        /// </remarks>
        public async Task<int> UnmonitorSatisfiedEditionsAsync(System.Threading.CancellationToken ct = default)
        {
            var satisfied = await _db.BookEditions
                .Where(e => e.Monitored
                            && e.Status == EditionWantedStatus.Imported
                            && e.Files.Any())
                .ToListAsync(ct);

            if (satisfied.Count == 0)
            {
                return 0;
            }

            foreach (var edition in satisfied)
            {
                edition.Monitored = false;
                edition.UpdatedAt = DateTime.UtcNow;
            }

            var touchedBookIds = satisfied.Select(e => e.BookId).Distinct().ToList();
            var books = await _db.Audiobooks
                .Include(a => a.Editions)
                .Where(a => a.Monitored && touchedBookIds.Contains(a.Id))
                .ToListAsync(ct);

            foreach (var book in books.Where(b => b.Editions.All(e => !e.Monitored)))
            {
                book.Monitored = false;
            }

            await _db.SaveChangesAsync(ct);
            return satisfied.Count;
        }
    }
}
