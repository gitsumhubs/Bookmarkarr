/* Bookmarkarr is licensed under the GNU AGPL v3 or later. */
namespace Bookmarkarr.Application.Search.Contracts;
public interface IEditionSearchService
{
    Task<SearchAndDownloadResult> SearchAndDownloadAsync(int editionId, CancellationToken ct = default);
}
