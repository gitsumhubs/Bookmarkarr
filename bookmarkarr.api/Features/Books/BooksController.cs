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
        [FromQuery] EditionMediaType? mediaType = null, [FromQuery] bool? wanted = null,
        CancellationToken ct = default, [FromQuery] int page = 1, [FromQuery] int pageSize = 100,
        [FromQuery] bool includeFiles = false, [FromQuery] string? query = null)
    {
        if (page < 1 || pageSize < 1 || pageSize > 500 || page > int.MaxValue / pageSize)
            return BadRequest(new { error = "Use page >= 1 and pageSize between 1 and 500." });
        var books = db.Audiobooks.AsNoTracking().AsQueryable();
        var wantedStatuses = new[] { EditionWantedStatus.Missing, EditionWantedStatus.Queued, EditionWantedStatus.Downloading,
            EditionWantedStatus.UpgradeAvailable, EditionWantedStatus.ImportBlocked, EditionWantedStatus.Failed };
        if (mediaType.HasValue || wanted.HasValue)
            books = books.Where(b => b.Editions.Any(e => (!mediaType.HasValue || e.MediaType == mediaType.Value)
                && (!wanted.HasValue || (e.Monitored && wantedStatuses.Contains(e.Status)) == wanted.Value)));
        if (!string.IsNullOrWhiteSpace(query)) books = books.Where(b => b.Title != null && b.Title.Contains(query));
        var total = await books.CountAsync(ct);
        var items = await books.OrderBy(b => b.Title).ThenBy(b => b.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(b => new BookDto(b.Id, b.Title ?? "", b.Authors ?? new List<string>(), b.GoodreadsId,
                b.Isbn ?? new List<string>(), b.ImageUrl, b.PublishedDate, b.Series,
                b.Editions.OrderBy(e => e.MediaType).Select(e => new BookEditionDto(e.Id, e.MediaType, e.Monitored,
                    e.UpgradeAllowed, e.Status, e.Monitored && wantedStatuses.Contains(e.Status), e.QualityProfileId, e.RootFolderId,
                    e.RootPath, e.DownloadCategory, e.LastSearchTime,
                    includeFiles ? e.Files.Select(f => new EditionFileDto(f.Id, f.Path, f.Extension, f.Size)).ToList() : new List<EditionFileDto>())).ToList()))
            .ToListAsync(ct);
        if (HttpContext is not null)
        {
            Response.Headers["X-Total-Count"] = total.ToString();
            Response.Headers["X-Page"] = page.ToString();
            Response.Headers["X-Page-Size"] = pageSize.ToString();
        }
        return Ok(items);
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
        var edition = await db.BookEditions.Include(e => e.Book).Include(e => e.Files).SingleOrDefaultAsync(e => e.Id == editionId, ct);
        if (edition is null) return NotFound();
        var error = await ValidateUpdateAsync(edition, request, ct);
        if (error is not null) return BadRequest(new { error });
        ApplyUpdate(edition, request);
        await db.SaveChangesAsync(ct);
        return Ok(ToEditionDto(edition));
    }

    [HttpPost("editions/bulk")]
    public async Task<IActionResult> BulkUpdate([FromBody] BulkEditionRequest request, CancellationToken ct)
    {
        var ids = request.EditionIds?.Distinct().ToList() ?? [];
        if (ids.Count == 0) return BadRequest(new { error = "At least one editionId is required." });
        var editions = await db.BookEditions.Include(e => e.Book).Include(e => e.Files).Where(e => ids.Contains(e.Id)).ToListAsync(ct);
        if (editions.Count != ids.Count) return NotFound(new { error = "One or more editions no longer exist." });
        var update = request.Update ?? new UpdateEditionRequest();
        foreach (var edition in editions)
        {
            var error = await ValidateUpdateAsync(edition, update, ct);
            if (error is not null) return BadRequest(new { error });
        }
        foreach (var edition in editions) ApplyUpdate(edition, update);
        await db.SaveChangesAsync(ct);
        return Ok(new { updated = editions.Count, requested = ids.Count });
    }

    /// <summary>Searches one edition without changing monitoring or grabbing a release.</summary>
    [HttpPost("editions/{editionId:int}/search")]
    public async Task<IActionResult> SearchEdition(int editionId, CancellationToken ct)
    {
        var edition = await db.BookEditions.Include(e => e.Book).Include(e => e.Files).SingleOrDefaultAsync(e => e.Id == editionId, ct);
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

    [HttpPost("editions/{editionId:int}/search-and-download")]
    public async Task<IActionResult> SearchAndDownloadEdition(int editionId, [FromServices] IEditionSearchService editionSearch, CancellationToken ct)
        => Ok(await editionSearch.SearchAndDownloadAsync(editionId, ct));

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
            BookEditionSynchronization.SetMonitoring(edition, request.Monitored.Value);
        }
        if (request.QualityProfileIdSet) edition.QualityProfileId = request.QualityProfileId;
        if (request.RootFolderIdSet) edition.RootFolderId = request.RootFolderId;
        if (request.RootPath is not null) edition.RootPath = request.RootPath;
        if (request.UpgradeAllowed.HasValue) edition.UpgradeAllowed = request.UpgradeAllowed.Value;
        if (request.Status.HasValue) edition.Status = request.Status.Value;
        if (!string.IsNullOrWhiteSpace(request.DownloadCategory)) edition.DownloadCategory = request.DownloadCategory.Trim();
        edition.UpdatedAt = DateTime.UtcNow;
        BookEditionSynchronization.SynchronizeAudioParent(edition);
    }

    private async Task<string?> ValidateUpdateAsync(BookEdition edition, UpdateEditionRequest request, CancellationToken ct)
    {
        if (request.QualityProfileIdSet && request.QualityProfileId.HasValue &&
            !await db.QualityProfiles.AnyAsync(p => p.Id == request.QualityProfileId && p.MediaType == edition.MediaType, ct))
            return $"Choose a {edition.MediaType} quality profile.";
        if (request.RootFolderIdSet && request.RootFolderId.HasValue)
        {
            var root = await db.RootFolders.SingleOrDefaultAsync(r => r.Id == request.RootFolderId && r.MediaType == edition.MediaType, ct);
            if (root is null) return $"Choose a {edition.MediaType} root folder.";
            request.RootPath ??= root.Path;
            try
            {
                var relative = Path.GetRelativePath(root.Path, request.RootPath);
                if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar) || Path.IsPathRooted(relative))
                    return "Root path must be inside the selected root folder.";
            }
            catch (ArgumentException) { return "Root path is invalid."; }
        }
        if (request.RootPath is not null && (string.IsNullOrWhiteSpace(request.RootPath) || !Path.IsPathFullyQualified(request.RootPath)))
            return "Root path must be a nonempty absolute path.";
        return null;
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
    private int? qualityProfileId;
    public int? QualityProfileId { get => qualityProfileId; set { qualityProfileId = value; QualityProfileIdSet = true; } }
    public bool QualityProfileIdSet { get; set; }
    private int? rootFolderId;
    public int? RootFolderId { get => rootFolderId; set { rootFolderId = value; RootFolderIdSet = true; } }
    public bool RootFolderIdSet { get; set; }
    public string? RootPath { get; set; }
    public string? DownloadCategory { get; set; }
}
public sealed record BulkEditionRequest(List<int>? EditionIds, UpdateEditionRequest? Update);
public sealed record RegisterEditionFileRequest(string Path, long Size);
