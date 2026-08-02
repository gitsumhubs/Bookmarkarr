/* Bookmarkarr is licensed under the GNU AGPL v3 or later. */
using System.Text.Json;
using System.Text;
using Bookmarkarr.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bookmarkarr.Api.Features.CatalogImports;

[ApiController]
[Route("api/v{version:apiVersion}/catalog-imports/goodreads")]
[Tags("Catalog Imports")]
public sealed class GoodreadsCatalogImportsController(
    BookmarkarrDbContext db,
    IHistoryRepository history,
    ILogger<GoodreadsCatalogImportsController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [HttpPost("preview")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(11 * 1024 * 1024)]
    public async Task<IActionResult> Preview([FromForm] IFormFile? file, CancellationToken ct)
    {
        await ExpireBatchesAsync(ct);
        if (file is null || file.Length == 0) return BadRequest(new { error = "A non-empty Goodreads CSV file is required." });
        if (file.Length > GoodreadsCsvParser.MaximumBytes) return StatusCode(413, new { error = "The CSV exceeds the 10 MB limit." });

        List<Dictionary<string, string>> csvRows;
        try { await using var stream = file.OpenReadStream(); csvRows = await GoodreadsCsvParser.ParseAsync(stream, ct); }
        catch (DecoderFallbackException) { return BadRequest(new { error = "The CSV must be valid UTF-8." }); }
        catch (FormatException ex) { return BadRequest(new { error = ex.Message }); }

        var books = await db.Audiobooks.AsNoTracking().Include(b => b.ExternalIdentifiers).Include(b => b.Editions).ToListAsync(ct);
        var rows = csvRows.Select((row, index) => BuildPreviewRow(row, index + 1, books)).ToList();
        var batch = new CatalogImportBatch { NormalizedRowsJson = JsonSerializer.Serialize(rows, JsonOptions) };
        db.CatalogImportBatches.Add(batch);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Created Goodreads preview batch {BatchId} with {RowCount} normalized rows", batch.Id, rows.Count);
        return Ok(ToPreviewResponse(batch, rows));
    }

    [HttpGet("{batchId}")]
    public async Task<IActionResult> Get(string batchId, CancellationToken ct)
    {
        await ExpireBatchesAsync(ct);
        var batch = await db.CatalogImportBatches.AsNoTracking().SingleOrDefaultAsync(b => b.Id == batchId, ct);
        if (batch is null) return NotFound();
        if (batch.Status == CatalogImportBatchStatus.Expired || batch.ExpiresAt <= DateTime.UtcNow) return StatusCode(410, new { error = "This import batch has expired." });
        var rows = JsonSerializer.Deserialize<List<GoodreadsPreviewRow>>(batch.NormalizedRowsJson, JsonOptions) ?? [];
        return Ok(ToPreviewResponse(batch, rows));
    }

    [HttpPost("{batchId}/commit")]
    public async Task<IActionResult> Commit(string batchId, [FromBody] GoodreadsCommitRequest request, CancellationToken ct)
    {
        await ExpireBatchesAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var batch = await db.CatalogImportBatches.SingleOrDefaultAsync(b => b.Id == batchId, ct);
        if (batch is null) return NotFound();
        if (batch.Status == CatalogImportBatchStatus.Expired || batch.ExpiresAt <= DateTime.UtcNow) return StatusCode(410, new { error = "This import batch has expired." });
        if (batch.Status == CatalogImportBatchStatus.Committed)
            return Ok(JsonSerializer.Deserialize<object>(batch.CommitSummaryJson ?? "{}", JsonOptions));

        var storedRows = JsonSerializer.Deserialize<List<GoodreadsPreviewRow>>(batch.NormalizedRowsJson, JsonOptions) ?? [];
        var choices = (request.Rows ?? []).ToDictionary(r => r.RowId);
        var books = await db.Audiobooks.Include(b => b.ExternalIdentifiers).Include(b => b.Editions).ToListAsync(ct);
        var createdBooks = 0;
        var createdEditions = 0;
        var unchanged = 0;

        foreach (var row in storedRows)
        {
            if (!choices.TryGetValue(row.RowId, out var choice) || !choice.Selected || !row.Eligible) continue;
            var media = (choice.MediaFormats ?? [EditionMediaType.Audiobook, EditionMediaType.Ebook]).Distinct().ToList();
            if (media.Count == 0) return BadRequest(new { error = $"Row {row.RowNumber} must request at least one media format." });
            if (row.MatchStatus == "ambiguous" && choice.ResolvedBookId is null)
                return Conflict(new { error = $"Row {row.RowNumber} has an ambiguous match and must be resolved before commit." });

            var targetId = choice.ResolvedBookId ?? row.MatchedBookId;
            var book = targetId is int id ? books.SingleOrDefault(b => b.Id == id) : null;
            if (targetId is not null && book is null) return BadRequest(new { error = $"Resolved book {targetId} for row {row.RowNumber} does not exist." });
            if (book is null)
            {
                book = new Audiobook
                {
                    Title = row.Title,
                    Authors = string.IsNullOrWhiteSpace(row.PrimaryAuthor) ? [] : [row.PrimaryAuthor],
                    Isbn = string.IsNullOrWhiteSpace(row.Isbn) ? [] : [row.Isbn],
                    GoodreadsId = row.GoodreadsId,
                    Monitored = media.Contains(EditionMediaType.Audiobook),
                    ExternalIdentifiers = []
                };
                if (!string.IsNullOrWhiteSpace(row.GoodreadsId)) book.ExternalIdentifiers.Add(NewIdentifier(book, AudiobookExternalIdentifierType.GoodreadsId, row.GoodreadsId));
                if (!string.IsNullOrWhiteSpace(row.Isbn)) book.ExternalIdentifiers.Add(NewIdentifier(book, AudiobookExternalIdentifierType.Isbn, row.Isbn));
                db.Audiobooks.Add(book);
                await db.SaveChangesAsync(ct);
                books.Add(book);
                createdBooks++;
            }

            var addedForBook = 0;
            foreach (var mediaType in media)
            {
                if (book.Editions.Any(e => e.MediaType == mediaType)) continue;
                var edition = BookEdition.Create(book.Id, mediaType);
                edition.RootPath = mediaType == EditionMediaType.Audiobook
                    ? Environment.GetEnvironmentVariable("BOOKMARKARR_AUDIOBOOK_ROOT") ?? "/audiobooks"
                    : Environment.GetEnvironmentVariable("BOOKMARKARR_EBOOK_ROOT") ?? "/ebooks";
                db.BookEditions.Add(edition);
                book.Editions.Add(edition);
                createdEditions++;
                addedForBook++;
                // Assign the edition key before writing its edition-aware history record.
                await db.SaveChangesAsync(ct);
                await history.AddAsync(new History
                {
                    AudiobookId = book.Id,
                    AudiobookTitle = book.Title,
                    EditionId = edition.Id,
                    MediaType = mediaType.ToString().ToLowerInvariant(),
                    EventType = "CatalogImportEditionAdded",
                    Source = "Goodreads",
                    Message = $"Added and monitored {mediaType.ToString().ToLowerInvariant()} edition from Goodreads CSV; automatic search was not started.",
                    Data = JsonSerializer.Serialize(new { batchId, row.RowNumber, row.GoodreadsId, row.Isbn }, JsonOptions)
                }, ct);
            }
            if (addedForBook == 0) unchanged++;
        }

        await db.SaveChangesAsync(ct);
        var summary = new GoodreadsCommitSummary(batch.Id, createdBooks, createdEditions, unchanged, DateTime.UtcNow);
        batch.Status = CatalogImportBatchStatus.Committed;
        batch.CommittedAt = summary.CommittedAt;
        batch.CommitSummaryJson = JsonSerializer.Serialize(summary, JsonOptions);
        batch.NormalizedRowsJson = "[]";
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Ok(summary);
    }

    private static AudiobookExternalIdentifier NewIdentifier(Audiobook book, AudiobookExternalIdentifierType type, string raw)
    {
        AudiobookIdentifierNormalizer.TryNormalize(type, raw, out var normalized, out _);
        return new AudiobookExternalIdentifier { AudiobookId = book.Id, Type = type, ValueRaw = raw, ValueNormalized = normalized, IsPrimary = true };
    }

    private static GoodreadsPreviewRow BuildPreviewRow(Dictionary<string, string> row, int rowNumber, List<Audiobook> books)
    {
        string Value(string key) => row.TryGetValue(key, out var v) ? v : string.Empty;
        var goodreadsId = new string(Value("book id").Where(char.IsDigit).ToArray());
        var title = Value("title").Trim();
        var author = (Value("author").Length > 0 ? Value("author") : Value("author l-f")).Trim();
        var isbn = GoodreadsCsvParser.NormalizeIsbn(Value("isbn13").Length > 0 ? Value("isbn13") : Value("isbn"));
        var eligible = title.Length > 0 && author.Length > 0;
        var reason = eligible ? null : "Title and primary author are required.";
        var candidates = new List<(Audiobook Book, string Method)>();

        if (goodreadsId.Length > 0)
            candidates = books.Where(b => b.GoodreadsId == goodreadsId || (b.ExternalIdentifiers?.Any(i => i.Type == AudiobookExternalIdentifierType.GoodreadsId && i.ValueNormalized == goodreadsId) ?? false)).Select(b => (b, "goodreadsId")).ToList();
        if (candidates.Count == 0 && isbn.Length > 0)
            candidates = books.Where(b => (b.Isbn?.Any(i => GoodreadsCsvParser.NormalizeIsbn(i) == isbn) ?? false) || (b.ExternalIdentifiers?.Any(i => i.Type == AudiobookExternalIdentifierType.Isbn && i.ValueNormalized == isbn) ?? false)).Select(b => (b, "isbn")).ToList();
        if (candidates.Count == 0 && eligible)
        {
            var nt = GoodreadsCsvParser.NormalizeText(title); var na = GoodreadsCsvParser.NormalizeText(author);
            candidates = books.Where(b => GoodreadsCsvParser.NormalizeText(b.Title) == nt && GoodreadsCsvParser.NormalizeText(b.Authors?.FirstOrDefault()) == na).Select(b => (b, "titleAuthor")).ToList();
        }

        return new GoodreadsPreviewRow(
            Guid.NewGuid().ToString("N"), rowNumber, goodreadsId, isbn, title, author, eligible, reason, true,
            [EditionMediaType.Audiobook, EditionMediaType.Ebook],
            candidates.Count switch { 0 => "new", 1 => "matched", _ => "ambiguous" },
            candidates.Count == 1 ? candidates[0].Book.Id : null,
            candidates.Count == 1 ? candidates[0].Method : null,
            candidates.Select(c => new GoodreadsMatchCandidate(c.Book.Id, c.Book.Title ?? string.Empty, c.Book.Authors?.FirstOrDefault() ?? string.Empty, c.Method)).ToList());
    }

    private static object ToPreviewResponse(CatalogImportBatch batch, List<GoodreadsPreviewRow> rows) => new
    {
        batchId = batch.Id, batch.ExpiresAt, batch.Status, rowCount = rows.Count,
        eligibleCount = rows.Count(r => r.Eligible), ambiguousCount = rows.Count(r => r.MatchStatus == "ambiguous"), rows
    };

    private async Task ExpireBatchesAsync(CancellationToken ct)
    {
        var expired = await db.CatalogImportBatches.Where(b => b.Status == CatalogImportBatchStatus.Preview && b.ExpiresAt <= DateTime.UtcNow).ToListAsync(ct);
        foreach (var batch in expired) { batch.Status = CatalogImportBatchStatus.Expired; batch.NormalizedRowsJson = "[]"; }
        if (expired.Count > 0) await db.SaveChangesAsync(ct);
    }
}

public sealed record GoodreadsMatchCandidate(int BookId, string Title, string PrimaryAuthor, string MatchMethod);
public sealed record GoodreadsPreviewRow(string RowId, int RowNumber, string GoodreadsId, string Isbn, string Title, string PrimaryAuthor,
    bool Eligible, string? IneligibleReason, bool Selected, List<EditionMediaType> MediaFormats, string MatchStatus,
    int? MatchedBookId, string? MatchMethod, List<GoodreadsMatchCandidate> MatchCandidates);
public sealed record GoodreadsCommitRow(string RowId, bool Selected, List<EditionMediaType>? MediaFormats, int? ResolvedBookId);
public sealed record GoodreadsCommitRequest(List<GoodreadsCommitRow>? Rows);
public sealed record GoodreadsCommitSummary(string BatchId, int CreatedBooks, int CreatedEditions, int UnchangedRows, DateTime CommittedAt);
