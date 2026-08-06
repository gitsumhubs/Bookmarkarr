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

namespace Bookmarkarr.Application.Audiobooks.Contracts
{
    /// <summary>What Audiobookshelf already knows about one folder on disk.</summary>
    /// <param name="Path">The library item's path, as Audiobookshelf reports it.</param>
    /// <param name="Title">Matched title.</param>
    /// <param name="Authors">Matched authors, in Audiobookshelf's order.</param>
    /// <param name="Series">Series name, when the item belongs to one.</param>
    /// <param name="SeriesSequence">Position within the series.</param>
    /// <param name="Asin">Audible identifier — the only exact key available, when present.</param>
    /// <param name="PublishedYear">Publication year, when known.</param>
    public sealed record AudiobookshelfItem(
        string Path,
        string? Title,
        IReadOnlyList<string> Authors,
        string? Series,
        string? SeriesSequence,
        string? Asin,
        string? PublishedYear);

    /// <summary>
    /// Reads Audiobookshelf's already-matched library.
    ///
    /// Audiobookshelf scans the same folders Bookmarkarr writes into and has already matched them
    /// against Audible metadata. Borrowing that turns "identify a book from an inconsistent folder
    /// name" into a lookup, which matters because real libraries mix
    /// <c>Author/Title</c>, <c>Series/Title</c>, and bare title folders in the same tree.
    ///
    /// Never authoritative on its own: Audiobookshelf scans on a schedule, so a book imported
    /// minutes ago may not appear yet. Disk decides what exists; this only says what it is.
    /// </summary>
    public interface IAudiobookshelfClient
    {
        /// <summary>Whether a url and token are configured.</summary>
        bool IsConfigured { get; }

        /// <summary>Verifies the server is reachable and the token is accepted.</summary>
        Task<(bool Success, string Message)> TestConnectionAsync(CancellationToken ct = default);

        /// <summary>
        /// Every library item, indexed by the path Audiobookshelf reports.
        ///
        /// Returns an empty index rather than throwing when unreachable or unconfigured, so an
        /// adoption scan degrades to folder-name matching instead of failing outright.
        /// </summary>
        Task<IReadOnlyDictionary<string, AudiobookshelfItem>> GetLibraryIndexAsync(CancellationToken ct = default);
    }
}
