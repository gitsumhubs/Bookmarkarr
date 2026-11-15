/* Bookmarkarr is licensed under the GNU AGPL v3 or later. */
namespace Bookmarkarr.Application.Search.Contracts;
public interface IEditionSearchService
{
    Task<SearchAndDownloadResult> SearchAndDownloadAsync(int editionId, CancellationToken ct = default);

    /// <summary>
    /// Searches for a book's audiobook edition. For callers that only know the book: the Wanted
    /// page, a retry after a failed download, and search-after-import. They used to run a second,
    /// older search that skipped the blocklist and the profile's minimum score.
    /// </summary>
    Task<SearchAndDownloadResult> SearchAndDownloadForBookAsync(int bookId, CancellationToken ct = default);
}
