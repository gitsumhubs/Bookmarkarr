/* Bookmarkarr is licensed under the GNU AGPL v3 or later. */
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Bookmarkarr.Domain.Books;

namespace Bookmarkarr.Tests.Features.Api.Features.Library;

public sealed class LibraryStatusReconciliationWorkflowTests
{
    [Fact]
    public async Task DryRun_PreviewsMissingSourceWithoutChangingDatabase()
    {
        await using var db = CreateDb();
        var (book, edition) = await SeedBookAsync(db, EditionWantedStatus.ImportBlocked);
        var download = await SeedDownloadAsync(db, book, edition, DownloadStatus.ImportBlocked);

        var result = await CreateWorkflow(db, LiveSnapshot()).ReconcileAsync(dryRun: true);

        Assert.True(result.DryRun);
        Assert.Equal(1, result.EditionsMarkedMissing);
        Assert.Equal(1, result.DownloadsMarkedSourceMissing);
        Assert.Equal(EditionWantedStatus.ImportBlocked, edition.Status);
        Assert.Equal(DownloadStatus.ImportBlocked, download.Status);
    }

    [Fact]
    public async Task Commit_MarksAbsentSourceAndEditionMissing()
    {
        await using var db = CreateDb();
        var (book, edition) = await SeedBookAsync(db, EditionWantedStatus.ImportBlocked);
        var download = await SeedDownloadAsync(db, book, edition, DownloadStatus.ImportBlocked);

        var result = await CreateWorkflow(db, LiveSnapshot()).ReconcileAsync(dryRun: false);

        Assert.False(result.DryRun);
        Assert.Equal(EditionWantedStatus.Missing, edition.Status);
        Assert.Equal(DownloadStatus.SourceMissing, download.Status);
        Assert.Null(download.ActiveAudiobookDeduplicationKey);
        Assert.Null(download.ImportBlockReason);
        Assert.Contains("no longer available", download.ErrorMessage);
    }

    [Fact]
    public async Task Commit_ClearsAbsentCompletedRowThatWouldOtherwisePinBlockedEdition()
    {
        await using var db = CreateDb();
        var (book, edition) = await SeedBookAsync(db, EditionWantedStatus.ImportBlocked);
        var blocked = await SeedDownloadAsync(db, book, edition, DownloadStatus.ImportBlocked);
        var completed = await SeedDownloadAsync(db, book, edition, DownloadStatus.Completed);

        var result = await CreateWorkflow(db, LiveSnapshot()).ReconcileAsync(dryRun: false);

        Assert.Equal(2, result.DownloadsMarkedSourceMissing);
        Assert.Equal(EditionWantedStatus.Missing, edition.Status);
        Assert.Equal(DownloadStatus.SourceMissing, blocked.Status);
        Assert.Equal(DownloadStatus.SourceMissing, completed.Status);
    }

    [Theory]
    [InlineData("cached", true, false)]
    [InlineData("unavailable", false, true)]
    public async Task Commit_DoesNotClearBlockedStatusWithoutFreshClientSnapshot(
        string snapshotState,
        bool stale,
        bool unavailable)
    {
        await using var db = CreateDb();
        var (book, edition) = await SeedBookAsync(db, EditionWantedStatus.ImportBlocked);
        var download = await SeedDownloadAsync(db, book, edition, DownloadStatus.ImportBlocked);
        var snapshot = LiveSnapshot();
        snapshot.Clients[0].SnapshotState = snapshotState;
        snapshot.Clients[0].IsStaleSnapshot = stale;
        snapshot.Clients[0].IsUnavailable = unavailable;

        var result = await CreateWorkflow(db, snapshot).ReconcileAsync(dryRun: false);

        Assert.Equal(1, result.DownloadsSkippedUnavailable);
        Assert.Equal(EditionWantedStatus.ImportBlocked, edition.Status);
        Assert.Equal(DownloadStatus.ImportBlocked, download.Status);
    }

    [Fact]
    public async Task Commit_RestoresBlockedStatusWhenSourceReappears()
    {
        await using var db = CreateDb();
        var (book, edition) = await SeedBookAsync(db, EditionWantedStatus.Missing);
        var download = await SeedDownloadAsync(db, book, edition, DownloadStatus.SourceMissing);
        var snapshot = LiveSnapshot(download.Id);

        var result = await CreateWorkflow(db, snapshot).ReconcileAsync(dryRun: false);

        Assert.Equal(1, result.DownloadsRestoredImportBlocked);
        Assert.Equal(1, result.EditionsRestoredImportBlocked);
        Assert.Equal(EditionWantedStatus.ImportBlocked, edition.Status);
        Assert.Equal(DownloadStatus.ImportBlocked, download.Status);
        Assert.Equal("Source available; import retry required", download.ImportBlockReason);
        Assert.Null(download.ErrorMessage);
    }

    [Fact]
    public async Task Commit_RegisteredFileWinsEvenWhenClientIsUnavailable()
    {
        await using var db = CreateDb();
        var (book, edition) = await SeedBookAsync(db, EditionWantedStatus.ImportBlocked);
        await SeedDownloadAsync(db, book, edition, DownloadStatus.ImportBlocked);
        edition.Files.Add(new EditionFile
        {
            Path = $"/library/{Guid.NewGuid():N}.m4b",
            Extension = ".m4b",
            Size = 42,
        });
        await db.SaveChangesAsync();
        var snapshot = LiveSnapshot();
        snapshot.Clients[0].SnapshotState = "unavailable";
        snapshot.Clients[0].IsUnavailable = true;

        await CreateWorkflow(db, snapshot).ReconcileAsync(dryRun: false);

        Assert.Equal(EditionWantedStatus.Imported, edition.Status);
    }

    [Fact]
    public async Task Commit_LegacyDownloadOnlyBlocksAudiobookEdition()
    {
        await using var db = CreateDb();
        var book = new Audiobook { Title = "Parity Book", Files = [], Editions = [] };
        var audiobookEdition = BookEdition.Create(0, EditionMediaType.Audiobook);
        var ebookEdition = BookEdition.Create(0, EditionMediaType.Ebook);
        audiobookEdition.Status = EditionWantedStatus.ImportBlocked;
        ebookEdition.Status = EditionWantedStatus.ImportBlocked;
        book.Editions.Add(audiobookEdition);
        book.Editions.Add(ebookEdition);
        db.Audiobooks.Add(book);
        await db.SaveChangesAsync();
        var download = await SeedDownloadAsync(db, book, edition: null, DownloadStatus.ImportBlocked);

        await CreateWorkflow(db, LiveSnapshot(download.Id)).ReconcileAsync(dryRun: false);

        Assert.Equal(EditionWantedStatus.ImportBlocked, audiobookEdition.Status);
        Assert.Equal(EditionWantedStatus.Missing, ebookEdition.Status);
    }

    private static BookmarkarrDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<BookmarkarrDbContext>()
            .UseInMemoryDatabase($"library-reconcile-{Guid.NewGuid():N}")
            .Options;
        return new BookmarkarrDbContext(options);
    }

    private static async Task<(Audiobook Book, BookEdition Edition)> SeedBookAsync(
        BookmarkarrDbContext db,
        EditionWantedStatus status)
    {
        var book = new Audiobook { Title = "Test Book", Files = [], Editions = [] };
        var edition = BookEdition.Create(0, EditionMediaType.Audiobook);
        edition.Status = status;
        book.Editions.Add(edition);
        db.Audiobooks.Add(book);
        await db.SaveChangesAsync();
        return (book, edition);
    }

    private static async Task<Download> SeedDownloadAsync(
        BookmarkarrDbContext db,
        Audiobook book,
        BookEdition? edition,
        DownloadStatus status)
    {
        var download = new Download
        {
            Id = $"download-{Guid.NewGuid():N}",
            AudiobookId = book.Id,
            EditionId = edition?.Id,
            ActiveAudiobookDeduplicationKey = book.Id,
            Title = book.Title ?? "Test Book",
            DownloadClientId = "client-1",
            Status = status,
            StartedAt = DateTime.UtcNow,
            ImportBlockReason = "No importable files",
            ImportBlockMessages = ["No importable files found"],
        };
        db.Downloads.Add(download);
        await db.SaveChangesAsync();
        return download;
    }

    private static LibraryStatusReconciliationWorkflow CreateWorkflow(
        BookmarkarrDbContext db,
        QueueSnapshot snapshot)
    {
        var queueService = new Mock<IDownloadQueueService>();
        queueService.Setup(service => service.GetQueueSnapshotAsync()).ReturnsAsync(snapshot);
        return new LibraryStatusReconciliationWorkflow(
            db,
            queueService.Object,
            NullLogger<LibraryStatusReconciliationWorkflow>.Instance);
    }

    private static QueueSnapshot LiveSnapshot(params string[] downloadIds) => new()
    {
        Items = downloadIds.Select(id => new QueueItem
        {
            Id = id,
            DownloadClientId = "client-1",
            SnapshotState = "live",
        }).ToList(),
        Clients =
        [
            new QueueClientStatus
            {
                ClientId = "client-1",
                ClientName = "Test client",
                ClientType = "nzbget",
                SnapshotState = "live",
            },
        ],
    };
}
