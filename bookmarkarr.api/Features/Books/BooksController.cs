/* Bookmarkarr is licensed under the GNU AGPL v3 or later. */
using Bookmarkarr.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bookmarkarr.Api.Features.Books;

[ApiController]
[Route("api/v{version:apiVersion}/books")]
[Tags("Books and Editions")]
public sealed class BooksController(BookmarkarrDbContext db, ISearchService searchService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<BookDto>>> List(
        [FromQuery] EditionMediaType? mediaType = null,
        [FromQuery] bool? wanted = null,
        CancellationToken ct = default)
    {
        var books = await db.Audiobooks.AsNoTracking().Include(b => b.Editions).ThenInclude(e => e.Files).OrderBy(b => b.Title).ToListAsync(ct);
        var dtos = books.Select(ToDto).ToList();
        if (mediaType.HasValue) dtos = dtos.Where(b => b.Editions.Any(e => e.MediaType == mediaType)).ToList();
        if (wanted.HasValue) dtos = dtos.Where(b => b.Editions.Any(e => e.IsWanted == wanted.Value)).ToList();
        return Ok(dtos);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<BookDto>> Get(int id, CancellationToken ct)
    {
        var book = await db.Audiobooks.AsNoTracking().Include(b => b.Editions).ThenInclude(e => e.Files).SingleOrDefaultAsync(b => b.Id == id, ct);
        return book is null ? NotFound() : Ok(ToDto(book));
    }

    [HttpPatch("editions/{editionId:int}")]
    public async Task<IActionResult> UpdateEdition(int editionId, [FromBody] UpdateEditionRequest request, CancellationToken ct)
    {
        var edition = await db.BookEditions.Include(e => e.Book).SingleOrDefaultAsync(e => e.Id == editionId, ct);
        if (edition is null) return NotFound();
        ApplyUpdate(edition, request);
        if (edition.MediaType == EditionMediaType.Audiobook)
        {
            // Keep the inherited 1.2.2 pipeline aligned with its audiobook edition only.
            edition.Book.Monitored = edition.Monitored;
            edition.Book.QualityProfileId = edition.QualityProfileId;
            if (!string.IsNullOrWhiteSpace(edition.RootPath)) edition.Book.BasePath = edition.RootPath;
        }
        await db.SaveChangesAsync(ct);
        return Ok(ToEditionDto(edition));
    }

    [HttpPost("editions/bulk")]
    public async Task<IActionResult> BulkUpdate([FromBody] BulkEditionRequest request, CancellationToken ct)
    {
        var ids = request.EditionIds?.Distinct().ToList() ?? [];
        if (ids.Count == 0) return BadRequest(new { error = "At least one editionId is required." });
        var editions = await db.BookEditions.Include(e => e.Book).Where(e => ids.Contains(e.Id)).ToListAsync(ct);
        foreach (var edition in editions) ApplyUpdate(edition, request.Update ?? new UpdateEditionRequest());
        await db.SaveChangesAsync(ct);
        return Ok(new { updated = editions.Count, requested = ids.Count });
    }

    /// <summary>Searches one edition without changing monitoring or grabbing a release.</summary>
    [HttpPost("editions/{editionId:int}/search")]
    public async Task<IActionResult> SearchEdition(int editionId, CancellationToken ct)
    {
        var edition = await db.BookEditions.Include(e => e.Book).SingleOrDefaultAsync(e => e.Id == editionId, ct);
        if (edition is null) return NotFound();
        var query = string.Join(' ', new[] { edition.Book.Title, edition.Book.Authors?.FirstOrDefault() }.Where(v => !string.IsNullOrWhiteSpace(v)));
        var contentType = edition.MediaType == EditionMediaType.Ebook ? SearchContentType.Ebook : SearchContentType.Audiobook;
        var request = new SearchRequest { Query = query, ContentType = contentType, IncludeEnrichment = false };
        var results = await searchService.SearchIndexersAsync(query, edition.DownloadCategory, request: request);
        edition.LastSearchTime = DateTime.UtcNow;
        edition.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(new { editionId, mediaType = edition.MediaType, results });
    }

    /// <summary>Registers only files valid for an edition's media type.</summary>
    [HttpPost("editions/{editionId:int}/files")]
    public async Task<IActionResult> RegisterFile(int editionId, [FromBody] RegisterEditionFileRequest request, CancellationToken ct)
    {
        var edition = await db.BookEditions.Include(e => e.Files).SingleOrDefaultAsync(e => e.Id == editionId, ct);
        if (edition is null) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Path) || !EditionFileTypes.IsAllowed(edition.MediaType, request.Path))
            return BadRequest(new { error = $"The file extension is not valid for a {edition.MediaType.ToString().ToLowerInvariant()} edition." });
        var normalized = Path.GetFullPath(request.Path);
        if (edition.Files.Any(f => string.Equals(f.Path, normalized, StringComparison.OrdinalIgnoreCase))) return Ok(ToEditionDto(edition));
        edition.Files.Add(new EditionFile { Path = normalized, Extension = Path.GetExtension(normalized).ToLowerInvariant(), Size = request.Size });
        edition.Status = EditionWantedStatus.Imported;
        edition.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(ToEditionDto(edition));
    }

    private static void ApplyUpdate(BookEdition edition, UpdateEditionRequest request)
    {
        if (request.Monitored.HasValue)
        {
            edition.Monitored = request.Monitored.Value;
            if (!edition.Monitored) edition.Status = EditionWantedStatus.Unmonitored;
            else if (edition.Status == EditionWantedStatus.Unmonitored) edition.Status = edition.Files.Count > 0 ? EditionWantedStatus.Imported : EditionWantedStatus.Missing;
        }
        if (request.QualityProfileIdSet) edition.QualityProfileId = request.QualityProfileId;
        if (request.RootFolderIdSet) edition.RootFolderId = request.RootFolderId;
        if (request.RootPath is not null) edition.RootPath = request.RootPath;
        if (request.UpgradeAllowed.HasValue) edition.UpgradeAllowed = request.UpgradeAllowed.Value;
        if (request.Status.HasValue) edition.Status = request.Status.Value;
        if (!string.IsNullOrWhiteSpace(request.DownloadCategory)) edition.DownloadCategory = request.DownloadCategory.Trim();
        edition.UpdatedAt = DateTime.UtcNow;
    }

    private static BookDto ToDto(Audiobook book) => new(book.Id, book.Title ?? string.Empty, book.Authors ?? [], book.GoodreadsId,
        book.Isbn ?? [], book.ImageUrl, book.PublishedDate, book.Series, book.Editions.Select(ToEditionDto).OrderBy(e => e.MediaType).ToList());

    private static BookEditionDto ToEditionDto(BookEdition edition) => new(edition.Id, edition.MediaType, edition.Monitored,
        edition.UpgradeAllowed, edition.Status, edition.IsWanted, edition.QualityProfileId, edition.RootFolderId, edition.RootPath,
        edition.DownloadCategory, edition.LastSearchTime, edition.Files.Select(f => new EditionFileDto(f.Id, f.Path, f.Extension, f.Size)).ToList());
}

public sealed record BookDto(int Id, string Title, List<string> Authors, string? GoodreadsId, List<string> Isbn,
    string? ImageUrl, string? PublishedDate, string? Series, List<BookEditionDto> Editions);
public sealed record BookEditionDto(int Id, EditionMediaType MediaType, bool Monitored, bool UpgradeAllowed,
    EditionWantedStatus Status, bool IsWanted, int? QualityProfileId, int? RootFolderId, string? RootPath,
    string DownloadCategory, DateTime? LastSearchTime, List<EditionFileDto> Files);
public sealed record EditionFileDto(int Id, string Path, string Extension, long Size);
public sealed class UpdateEditionRequest
{
    public bool? Monitored { get; set; }
    public bool? UpgradeAllowed { get; set; }
    public EditionWantedStatus? Status { get; set; }
    public int? QualityProfileId { get; set; }
    public bool QualityProfileIdSet { get; set; }
    public int? RootFolderId { get; set; }
    public bool RootFolderIdSet { get; set; }
    public string? RootPath { get; set; }
    public string? DownloadCategory { get; set; }
}
public sealed record BulkEditionRequest(List<int>? EditionIds, UpdateEditionRequest? Update);
public sealed record RegisterEditionFileRequest(string Path, long Size);
