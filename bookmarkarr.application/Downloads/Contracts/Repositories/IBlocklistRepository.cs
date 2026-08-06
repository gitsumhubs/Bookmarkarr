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

namespace Bookmarkarr.Application.Downloads.Contracts.Repositories
{
    public interface IBlocklistRepository
    {
        Task<List<BlocklistEntry>> GetAllAsync(CancellationToken ct = default);

        /// <summary>Entries recorded against one book. Blocklisting is always per-book.</summary>
        Task<List<BlocklistEntry>> GetByAudiobookIdAsync(int audiobookId, CancellationToken ct = default);

        Task<BlocklistEntry?> GetByIdAsync(int id, CancellationToken ct = default);

        Task<BlocklistEntry> AddAsync(BlocklistEntry entry, CancellationToken ct = default);

        Task<bool> DeleteAsync(int id, CancellationToken ct = default);

        /// <summary>Clears every entry for a book, so a manual retry starts from a clean slate.</summary>
        Task<int> DeleteByAudiobookIdAsync(int audiobookId, CancellationToken ct = default);
    }
}
