/* Bookmarkarr is licensed under the GNU AGPL v3 or later. */
using Microsoft.EntityFrameworkCore;

namespace Bookmarkarr.Infrastructure.Persistence.Repositories;

internal static class AudiobookQueryExtensions
{
    /// <summary>
    /// Eager-loads the per-edition data callers need alongside a book.
    /// </summary>
    /// <remarks>
    /// The quality profile matters as much as the files: the edition owns the profile in
    /// Bookmarkarr's model, and automatic search falls back to it when the legacy
    /// book-level column is unset. Leaving it unloaded makes that fallback silently
    /// see null and report the book as having no profile at all.
    /// </remarks>
    public static IQueryable<Audiobook> IncludeEditionDetails(this IQueryable<Audiobook> query) =>
        query
            .Include(a => a.Editions).ThenInclude(e => e.Files)
            .Include(a => a.Editions).ThenInclude(e => e.QualityProfile);
}
