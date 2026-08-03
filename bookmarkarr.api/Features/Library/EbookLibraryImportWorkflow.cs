/* Bookmarkarr is licensed under the GNU AGPL v3 or later. */
using Bookmarkarr.Domain.Common;
using Bookmarkarr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Bookmarkarr.Api.Features.Library;

/// <summary>
/// Imports ebook files that already exist on disk into a book's ebook edition.
/// </summary>
/// <remarks>
/// This is the bulk-import counterpart to a downloaded ebook. It deliberately reuses
/// <see cref="IDownloadImportService"/> for the file placement so an imported-from-disk
/// ebook lands in exactly the same location, with the same naming, as one that arrived
/// through a download - there is one implementation of "where does an ebook go".
///
/// It is separate from the audiobook manual-import path on purpose. That path creates
/// <c>AudiobookFile</c> records carrying audio-only metadata (duration, bitrate, sample
/// rate, channels) and probes files with ffprobe. Ebooks belong in <c>EditionFile</c>
/// rows on the ebook edition and have nothing for ffprobe to read.
/// </remarks>
public sealed class EbookLibraryImportWorkflow(
    BookmarkarrDbContext db,
    IAudiobookRepository audiobookRepository,
    IDownloadImportService importService,
    IRootFolderService rootFolderService,
    IHistoryRepository history,
    IFileSystem fileSystem,
    ILogger<EbookLibraryImportWorkflow> logger)
{
    public async Task<EbookImportOutcome> ImportAsync(
        int bookId,
        IReadOnlyList<string> sourceFiles,
        FileAction action,
        CancellationToken ct)
    {
        var audiobook = await audiobookRepository.GetByIdAsync(bookId);
        if (audiobook is null)
        {
            return EbookImportOutcome.Failed($"Book {bookId} was not found.");
        }

        // Only ever accept recognised ebook formats here. Without this an audio file
        // could be registered against an ebook edition, which is the exact boundary
        // the media split exists to protect.
        var ebookFiles = sourceFiles
            .Where(path => !string.IsNullOrWhiteSpace(path) && FileUtils.IsEbookFile(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ebookFiles.Count == 0)
        {
            return EbookImportOutcome.Failed("No recognized ebook files were supplied.");
        }

        var ebookRoot = await rootFolderService.GetDefaultAsync(EditionMediaType.Ebook);
        if (ebookRoot is null || string.IsNullOrWhiteSpace(ebookRoot.Path))
        {
            return EbookImportOutcome.Failed(
                "No ebook root folder is configured. Add one under Settings before importing ebooks.");
        }

        var edition = await EnsureEbookEditionAsync(audiobook, ebookRoot, ct);

        var results = await importService.ImportDownloadFilesAsync(
            audiobook,
            ebookFiles,
            ct,
            new DownloadImportOptions(
                ContentType: DownloadContentTypes.Ebook,
                LibraryRoot: ebookRoot.Path,
                CompletedFileActionOverride: action));

        var imported = new List<string>();
        foreach (var result in results.Where(r => r.Success && !string.IsNullOrWhiteSpace(r.FinalPath)))
        {
            var finalPath = Path.GetFullPath(result.FinalPath!);

            // Re-importing the same file must not create a duplicate row.
            if (edition.Files.Any(f => string.Equals(f.Path, finalPath, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            edition.Files.Add(new EditionFile
            {
                Path = finalPath,
                Extension = Path.GetExtension(finalPath).ToLowerInvariant(),
                Size = fileSystem.FileExists(finalPath) ? fileSystem.GetFileLength(finalPath) : 0
            });
            imported.Add(finalPath);
        }

        if (imported.Count > 0)
        {
            edition.Status = EditionWantedStatus.Imported;
            edition.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            await history.AddAsync(new History
            {
                AudiobookId = audiobook.Id,
                AudiobookTitle = audiobook.Title ?? "Unknown",
                EditionId = edition.Id,
                MediaType = "ebook",
                EventType = "File Added",
                Source = "Library Import",
                Message = $"Imported {imported.Count} ebook file(s) from the library scan."
            }, ct);

            logger.LogInformation(
                "Imported {Count} ebook file(s) into edition {EditionId} for book {BookId}",
                imported.Count, edition.Id, audiobook.Id);
        }

        var failures = results
            .Where(r => !r.Success)
            .Select(r => r.Message ?? "Import failed")
            .ToList();

        return new EbookImportOutcome(imported.Count > 0, imported.Count, edition.Id, failures);
    }

    /// <summary>
    /// Returns the book's ebook edition, creating it when the book is audiobook-only.
    /// </summary>
    private async Task<BookEdition> EnsureEbookEditionAsync(
        Audiobook audiobook, RootFolder ebookRoot, CancellationToken ct)
    {
        var edition = await db.BookEditions
            .Include(e => e.Files)
            .FirstOrDefaultAsync(e => e.BookId == audiobook.Id && e.MediaType == EditionMediaType.Ebook, ct);

        if (edition is not null)
        {
            return edition;
        }

        edition = BookEdition.Create(audiobook.Id, EditionMediaType.Ebook);
        edition.RootFolderId = ebookRoot.Id;
        edition.RootPath = ebookRoot.Path;
        db.BookEditions.Add(edition);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Created ebook edition {EditionId} for book {BookId} during library import",
            edition.Id, audiobook.Id);
        return edition;
    }
}

public sealed record EbookImportOutcome(
    bool Success,
    int ImportedCount,
    int? EditionId,
    IReadOnlyList<string> Errors)
{
    public static EbookImportOutcome Failed(string error) => new(false, 0, null, [error]);
}
