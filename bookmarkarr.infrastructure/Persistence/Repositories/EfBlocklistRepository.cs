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
    public class EfBlocklistRepository(BookmarkarrDbContext db) : IBlocklistRepository
    {
        public async Task<List<BlocklistEntry>> GetAllAsync(CancellationToken ct = default) =>
            await db.BlocklistEntries
                .AsNoTracking()
                .OrderByDescending(entry => entry.UpdatedAt)
                .ToListAsync(ct);

        public async Task<List<BlocklistEntry>> GetByAudiobookIdAsync(int audiobookId, CancellationToken ct = default) =>
            await db.BlocklistEntries
                .AsNoTracking()
                .Where(entry => entry.AudiobookId == audiobookId)
                .ToListAsync(ct);

        public async Task<BlocklistEntry?> GetByIdAsync(int id, CancellationToken ct = default) =>
            await db.BlocklistEntries.FindAsync([id], ct);

        public async Task<BlocklistEntry> AddAsync(BlocklistEntry entry, CancellationToken ct = default)
        {
            db.BlocklistEntries.Add(entry);
            await db.SaveChangesAsync(ct);
            return entry;
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
        {
            var entry = await db.BlocklistEntries.FindAsync([id], ct);
            if (entry == null)
            {
                return false;
            }

            db.BlocklistEntries.Remove(entry);
            await db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<int> DeleteByAudiobookIdAsync(int audiobookId, CancellationToken ct = default)
        {
            var entries = await db.BlocklistEntries
                .Where(entry => entry.AudiobookId == audiobookId)
                .ToListAsync(ct);

            if (entries.Count == 0)
            {
                return 0;
            }

            db.BlocklistEntries.RemoveRange(entries);
            await db.SaveChangesAsync(ct);
            return entries.Count;
        }
    }
}
