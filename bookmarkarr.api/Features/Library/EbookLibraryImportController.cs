/* Bookmarkarr is licensed under the GNU AGPL v3 or later. */
using Microsoft.AspNetCore.Mvc;

namespace Bookmarkarr.Api.Features.Library;

/// <summary>
/// Bulk import of ebook files that already exist on disk.
/// </summary>
/// <remarks>
/// Pairs with the unmatched scan of an ebook root folder: the scan reports ebook files
/// that are not yet in the library, the user matches them to a book, and this endpoint
/// files them into that book's ebook edition.
/// </remarks>
[ApiController]
[Route("api/v{version:apiVersion}/library/ebook-import")]
[Tags("Library")]
public sealed class EbookLibraryImportController(
    EbookLibraryImportWorkflow workflow,
    ILogger<EbookLibraryImportController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Import(
        [FromBody] EbookLibraryImportRequest request, CancellationToken ct)
    {
        if (request.BookId <= 0)
        {
            return BadRequest(new { error = "A bookId is required." });
        }

        if (request.SourceFiles is null || request.SourceFiles.Count == 0)
        {
            return BadRequest(new { error = "At least one source file is required." });
        }

        var outcome = await workflow.ImportAsync(request.BookId, request.SourceFiles, request.Action, ct);

        if (!outcome.Success)
        {
            logger.LogWarning(
                "Ebook library import for book {BookId} imported nothing: {Errors}",
                request.BookId, string.Join("; ", outcome.Errors));
            return BadRequest(new { error = outcome.Errors.FirstOrDefault() ?? "Import failed.", errors = outcome.Errors });
        }

        return Ok(new
        {
            imported = outcome.ImportedCount,
            editionId = outcome.EditionId,
            errors = outcome.Errors
        });
    }
}

/// <param name="BookId">Book the ebook files belong to.</param>
/// <param name="SourceFiles">Absolute paths of the ebook files to import.</param>
/// <param name="Action">Copy, Move, or Hardlink. Defaults to Copy so a failed import
/// never destroys the user's only copy of the file.</param>
public sealed record EbookLibraryImportRequest(
    int BookId,
    List<string> SourceFiles,
    FileAction Action = FileAction.Copy);
