/* Bookmarkarr is licensed under the GNU AGPL v3 or later. */
using Bookmarkarr.Api.Features.Books;
using Bookmarkarr.Domain.Books;
using Bookmarkarr.Infrastructure.Persistence.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
namespace Bookmarkarr.Tests.Features.Api.Features.Books;

public sealed class EditionWorkflowTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private BookmarkarrDbContext db = null!;
    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        db = new(new DbContextOptionsBuilder<BookmarkarrDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
    }
    public async Task DisposeAsync() { await db.DisposeAsync(); await connection.DisposeAsync(); }
    private BooksController Controller() => new(db, Mock.Of<ISearchService>()) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
    private async Task<Audiobook> Book(string title = "Example")
    {
        var book = new Audiobook { Title = title, BasePath = "/audiobooks/Author/Example", Monitored = true,
            Editions = [new() { MediaType = EditionMediaType.Audiobook, Monitored = true, RootPath = "/audiobooks" },
                new() { MediaType = EditionMediaType.Ebook, Monitored = true, RootPath = "/ebooks" }] };
        db.Audiobooks.Add(book); await db.SaveChangesAsync(); return book;
    }
    [Fact]
    public async Task IndividualAndBulkMonitoringPreserveBookDirectoryAndSibling()
    {
        var book = await Book(); var audio = book.Editions.Single(e => e.MediaType == EditionMediaType.Audiobook);
        var controller = Controller();
        Assert.IsType<OkObjectResult>(await controller.UpdateEdition(audio.Id, new() { Monitored = false }, default));
        Assert.False(book.Monitored); Assert.True(book.Editions.Single(e => e.MediaType == EditionMediaType.Ebook).Monitored);
        Assert.Equal("/audiobooks/Author/Example", book.BasePath);
        Assert.IsType<OkObjectResult>(await controller.BulkUpdate(new([audio.Id], new() { Monitored = true }), default));
        Assert.True(book.Monitored); Assert.Equal("/audiobooks/Author/Example", book.BasePath);
    }
    [Fact]
    public async Task WrongMediaProfileOrRootIsRejectedAtomically()
    {
        var book = await Book(); var ebook = book.Editions.Single(e => e.MediaType == EditionMediaType.Ebook);
        var profile = new QualityProfile { Name = "Audio", MediaType = EditionMediaType.Audiobook };
        var root = new RootFolder { Name = "Audio", Path = "/audiobooks", MediaType = EditionMediaType.Audiobook };
        db.QualityProfiles.Add(profile); db.RootFolders.Add(root); await db.SaveChangesAsync();
        var controller = Controller();
        Assert.IsType<BadRequestObjectResult>(await controller.UpdateEdition(ebook.Id, new() { QualityProfileId = profile.Id, Monitored = false }, default));
        Assert.True(ebook.Monitored); Assert.Null(ebook.QualityProfileId);
        Assert.IsType<BadRequestObjectResult>(await controller.BulkUpdate(new([ebook.Id], new() { RootFolderId = root.Id }), default));
        Assert.Null(ebook.RootFolderId);
    }
    [Fact]
    public async Task RemonitoringUsesExistingFiles()
    {
        var book = await Book(); var audio = book.Editions[0];
        audio.Monitored = false; audio.Status = EditionWantedStatus.Unmonitored;
        audio.Files.Add(new EditionFile { Path = "/audiobooks/Author/Example/book.m4b", Extension = ".m4b" }); await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.IsType<OkObjectResult>(await Controller().UpdateEdition(audio.Id, new() { Monitored = true }, default));
        Assert.Equal(EditionWantedStatus.Imported, (await db.BookEditions.FindAsync(audio.Id))!.Status);
    }
    [Fact]
    public async Task PaginationFiltersSameEditionAndOmitsFileDetailsByDefault()
    {
        var a = await Book("A"); var b = await Book("B");
        a.Editions[0].Status = EditionWantedStatus.Missing; a.Editions[1].Status = EditionWantedStatus.Imported;
        b.Editions[0].Status = EditionWantedStatus.Imported; b.Editions[1].Status = EditionWantedStatus.Missing;
        b.Editions[1].Files.Add(new EditionFile { Path = "/ebooks/B.epub", Extension = ".epub" }); await db.SaveChangesAsync();
        var controller = Controller();
        var response = await controller.List(EditionMediaType.Ebook, true, default, pageSize: 1);
        var books = Assert.IsType<List<BookDto>>(Assert.IsType<OkObjectResult>(response.Result).Value);
        Assert.Equal(b.Id, Assert.Single(books).Id); Assert.All(books[0].Editions, e => Assert.Empty(e.Files));
        Assert.Equal("1", controller.Response.Headers["X-Total-Count"]);
        var second = await controller.List(ct: default, page: 2, pageSize: 1);
        Assert.Equal(b.Id, Assert.Single(Assert.IsType<List<BookDto>>(Assert.IsType<OkObjectResult>(second.Result).Value)).Id);
    }
    [Fact]
    public async Task LegacyIsbnArraysAreMatchedInSql()
    {
        var book = await Book(); book.Isbn = ["978-0-123-45678-9"]; await db.SaveChangesAsync();
        var repository = new AudiobookRepository(db);
        Assert.Equal(book.Id, (await repository.GetByIsbnAsync("9780123456789"))!.Id);
    }
    [Fact]
    public async Task MonitorQueryLoadsOnlyActiveAndDeferredRemovalRows()
    {
        db.Downloads.AddRange(new Download { Id = "active", DownloadClientId = "keep", Status = DownloadStatus.Downloading },
            new Download { Id = "imported", DownloadClientId = "keep", Status = DownloadStatus.Moved },
            new Download { Id = "deferred", DownloadClientId = "remove", Status = DownloadStatus.Moved },
            new Download { Id = "disabled", DownloadClientId = "disabled", Status = DownloadStatus.Downloading });
        await db.SaveChangesAsync();
        var repository = new EfDownloadRepository(new Factory(connection), NullLogger<EfDownloadRepository>.Instance);
        var candidates = await repository.GetMonitorCandidatesAsync(["keep", "remove"], ["remove"]);
        Assert.Equal(new[] { "active", "deferred" }, candidates.Select(d => d.Id).Order().ToArray());
    }
    [Fact]
    public async Task LiveIdMatchingLoadsOnlyRelevantHistoricalRows()
    {
        db.Downloads.AddRange(new Download { Id = "old", Status = DownloadStatus.Moved, DownloadClientId = "client", Metadata = new() { ["TorrentHash"] = "old-hash" } },
            new Download { Id = "live", Status = DownloadStatus.Moved, DownloadClientId = "client", Metadata = new() { ["TorrentHash"] = "LIVE-HASH" } });
        await db.SaveChangesAsync();
        var repository = new EfDownloadRepository(new Factory(connection), NullLogger<EfDownloadRepository>.Instance);
        Assert.Equal("live", Assert.Single(await repository.GetQueueMatchingCandidatesAsync(["live-hash"])).Id);
        Assert.Equal("LIVE-HASH", Assert.Single(await repository.GetKnownClientItemIdsAsync(["live-hash"])));
    }
    [Fact]
    public async Task MissingDownloadReleasesEditionAndDeduplicationKey()
    {
        var book = await Book(); var edition = book.Editions[0]; edition.Status = EditionWantedStatus.Downloading;
        await db.SaveChangesAsync();
        var repository = new EfDownloadRepository(new Factory(connection), NullLogger<EfDownloadRepository>.Instance);
        var download = new Download { Id = "missing", AudiobookId = book.Id, EditionId = edition.Id, DownloadClientId = "client", Status = DownloadStatus.Downloading };
        await repository.AddAsync(download);
        download.Status = DownloadStatus.SourceMissing;
        await repository.UpdateAsync(download);
        db.ChangeTracker.Clear();
        Assert.Equal(EditionWantedStatus.Failed, (await db.BookEditions.FindAsync(edition.Id))!.Status);
        Assert.Null((await repository.GetByIdAsync(download.Id))!.ActiveAudiobookDeduplicationKey);
    }
    private sealed class Factory(SqliteConnection connection) : IDbContextFactory<BookmarkarrDbContext>
    {
        public BookmarkarrDbContext CreateDbContext() => new(new DbContextOptionsBuilder<BookmarkarrDbContext>().UseSqlite(connection).Options);
        public Task<BookmarkarrDbContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(CreateDbContext());
    }

}
