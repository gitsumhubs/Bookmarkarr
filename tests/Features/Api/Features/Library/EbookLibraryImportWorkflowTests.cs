/* Bookmarkarr is licensed under the GNU AGPL v3 or later. */
using Bookmarkarr.Domain.Books;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bookmarkarr.Tests.Features.Api.Features.Library;

public sealed class EbookLibraryImportWorkflowTests : IAsyncLifetime
{
    private readonly string _databasePath =
        Path.Join(Path.GetTempPath(), "bookmarkarr-tests", $"ebook-import-{Guid.NewGuid():N}.db");
    private readonly string _workRoot =
        Path.Join(Path.GetTempPath(), "bookmarkarr-tests", $"ebook-import-work-{Guid.NewGuid():N}");

    private DbContextOptions<BookmarkarrDbContext> _options = null!;
    private string _ebookRoot = null!;
    private string _sourceRoot = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        _ebookRoot = Path.Join(_workRoot, "ebooks");
        _sourceRoot = Path.Join(_workRoot, "incoming");
        Directory.CreateDirectory(_ebookRoot);
        Directory.CreateDirectory(_sourceRoot);

        _options = new DbContextOptionsBuilder<BookmarkarrDbContext>()
            .UseSqlite($"Data Source={_databasePath};Pooling=False")
            .Options;
        await using var db = new BookmarkarrDbContext(_options);
        await db.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync()
    {
        if (File.Exists(_databasePath)) File.Delete(_databasePath);
        if (Directory.Exists(_workRoot)) Directory.Delete(_workRoot, recursive: true);
        return Task.CompletedTask;
    }

    private static IFileSystem CreateFileSystem()
    {
        var fileSystem = new Mock<IFileSystem>();
        fileSystem.Setup(fs => fs.FileExists(It.IsAny<string>())).Returns((string p) => File.Exists(p));
        fileSystem.Setup(fs => fs.GetFileLength(It.IsAny<string>()))
            .Returns((string p) => File.Exists(p) ? new FileInfo(p).Length : 0);
        return fileSystem.Object;
    }

    private string CreateSourceFile(string name)
    {
        var path = Path.Join(_sourceRoot, name);
        File.WriteAllText(path, "content");
        return path;
    }

    /// <summary>
    /// Stands in for the real import service, reporting where each file would land.
    /// </summary>
    private sealed class FakeImportService : IDownloadImportService
    {
        private readonly string _libraryRoot;
        public FileAction? ObservedAction { get; private set; }
        public string? ObservedContentType { get; private set; }

        public FakeImportService(string libraryRoot) => _libraryRoot = libraryRoot;

        public Task<List<ImportResult>> ImportDownloadFilesAsync(
            Audiobook audiobook,
            List<string> files,
            CancellationToken ct = default,
            DownloadImportOptions? options = null)
        {
            ObservedAction = options?.CompletedFileActionOverride;
            ObservedContentType = options?.ContentType;

            var results = new List<ImportResult>();
            foreach (var file in files)
            {
                var destination = Path.Join(_libraryRoot, Path.GetFileName(file));
                File.Copy(file, destination, overwrite: true);
                results.Add(ImportResult.ImportSuccess(FileAction.Copy, file, destination));
            }
            return Task.FromResult(results);
        }
    }

    private (EbookLibraryImportWorkflow Workflow, FakeImportService Import, BookmarkarrDbContext Db)
        CreateWorkflow(BookmarkarrDbContext db, RootFolder ebookRoot)
    {
        var import = new FakeImportService(_ebookRoot);
        var rootService = new Mock<IRootFolderService>();
        rootService.Setup(s => s.GetDefaultAsync(EditionMediaType.Ebook)).ReturnsAsync(ebookRoot);

        var audiobookRepo = new Mock<IAudiobookRepository>();
        audiobookRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>()))
            .ReturnsAsync((int id) => db.Audiobooks.FirstOrDefault(a => a.Id == id));

        var history = new Mock<IHistoryRepository>();

        var workflow = new EbookLibraryImportWorkflow(
            db, audiobookRepo.Object, import, rootService.Object, history.Object,
            CreateFileSystem(), NullLogger<EbookLibraryImportWorkflow>.Instance);

        return (workflow, import, db);
    }

    private async Task<(Audiobook Book, RootFolder Root)> SeedAsync(BookmarkarrDbContext db)
    {
        var root = new RootFolder
        {
            Name = "Ebooks", Path = _ebookRoot, MediaType = EditionMediaType.Ebook, IsDefault = true
        };
        db.RootFolders.Add(root);
        var book = new Audiobook { Title = "Dune", Authors = ["Frank Herbert"], Editions = [] };
        db.Audiobooks.Add(book);
        await db.SaveChangesAsync();
        return (book, root);
    }

    [Fact]
    public async Task ImportsEbookAndCreatesTheEditionWhenTheBookIsAudiobookOnly()
    {
        await using var db = new BookmarkarrDbContext(_options);
        var (book, root) = await SeedAsync(db);
        var (workflow, import, _) = CreateWorkflow(db, root);
        var source = CreateSourceFile("dune.epub");

        var outcome = await workflow.ImportAsync(book.Id, [source], FileAction.Copy, CancellationToken.None);

        Assert.True(outcome.Success);
        Assert.Equal(1, outcome.ImportedCount);
        Assert.Equal(DownloadContentTypes.Ebook, import.ObservedContentType);

        var edition = await db.BookEditions.Include(e => e.Files)
            .SingleAsync(e => e.BookId == book.Id && e.MediaType == EditionMediaType.Ebook);
        Assert.Equal(EditionWantedStatus.Imported, edition.Status);
        Assert.Equal(_ebookRoot, edition.RootPath);
        var file = Assert.Single(edition.Files);
        Assert.Equal(".epub", file.Extension);
    }

    [Fact]
    public async Task ReusesAnExistingEbookEditionRatherThanCreatingASecond()
    {
        await using var db = new BookmarkarrDbContext(_options);
        var (book, root) = await SeedAsync(db);
        var existing = BookEdition.Create(book.Id, EditionMediaType.Ebook);
        db.BookEditions.Add(existing);
        await db.SaveChangesAsync();

        var (workflow, _, _) = CreateWorkflow(db, root);
        var outcome = await workflow.ImportAsync(
            book.Id, [CreateSourceFile("dune.epub")], FileAction.Copy, CancellationToken.None);

        Assert.True(outcome.Success);
        Assert.Equal(existing.Id, outcome.EditionId);
        Assert.Single(await db.BookEditions
            .Where(e => e.BookId == book.Id && e.MediaType == EditionMediaType.Ebook).ToListAsync());
    }

    [Fact]
    public async Task ReimportingTheSameFileDoesNotDuplicateTheEditionFile()
    {
        await using var db = new BookmarkarrDbContext(_options);
        var (book, root) = await SeedAsync(db);
        var (workflow, _, _) = CreateWorkflow(db, root);
        var source = CreateSourceFile("dune.epub");

        await workflow.ImportAsync(book.Id, [source], FileAction.Copy, CancellationToken.None);
        await workflow.ImportAsync(book.Id, [source], FileAction.Copy, CancellationToken.None);

        var edition = await db.BookEditions.Include(e => e.Files)
            .SingleAsync(e => e.BookId == book.Id && e.MediaType == EditionMediaType.Ebook);
        Assert.Single(edition.Files);
    }

    [Theory]
    [InlineData("chapter.mp3")]
    [InlineData("chapter.m4b")]
    [InlineData("cover.jpg")]
    public async Task RejectsNonEbookFiles(string fileName)
    {
        await using var db = new BookmarkarrDbContext(_options);
        var (book, root) = await SeedAsync(db);
        var (workflow, _, _) = CreateWorkflow(db, root);

        var outcome = await workflow.ImportAsync(
            book.Id, [CreateSourceFile(fileName)], FileAction.Copy, CancellationToken.None);

        // Audio must never be registered against an ebook edition.
        Assert.False(outcome.Success);
        Assert.Empty(await db.EditionFiles.ToListAsync());
    }

    [Fact]
    public async Task FailsClearlyWhenNoEbookRootIsConfigured()
    {
        await using var db = new BookmarkarrDbContext(_options);
        var (book, _) = await SeedAsync(db);

        var rootService = new Mock<IRootFolderService>();
        rootService.Setup(s => s.GetDefaultAsync(EditionMediaType.Ebook)).ReturnsAsync((RootFolder?)null);
        var audiobookRepo = new Mock<IAudiobookRepository>();
        audiobookRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>())).ReturnsAsync(book);

        var workflow = new EbookLibraryImportWorkflow(
            db, audiobookRepo.Object, new FakeImportService(_ebookRoot), rootService.Object,
            new Mock<IHistoryRepository>().Object, CreateFileSystem(),
            NullLogger<EbookLibraryImportWorkflow>.Instance);

        var outcome = await workflow.ImportAsync(
            book.Id, [CreateSourceFile("dune.epub")], FileAction.Copy, CancellationToken.None);

        Assert.False(outcome.Success);
        Assert.Contains(outcome.Errors, e => e.Contains("root folder", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PassesTheRequestedFileActionThrough()
    {
        await using var db = new BookmarkarrDbContext(_options);
        var (book, root) = await SeedAsync(db);
        var (workflow, import, _) = CreateWorkflow(db, root);

        await workflow.ImportAsync(
            book.Id, [CreateSourceFile("dune.epub")], FileAction.Move, CancellationToken.None);

        Assert.Equal(FileAction.Move, import.ObservedAction);
    }

    [Fact]
    public async Task MissingBookIsReportedRatherThanThrowing()
    {
        await using var db = new BookmarkarrDbContext(_options);
        var (_, root) = await SeedAsync(db);
        var (workflow, _, _) = CreateWorkflow(db, root);

        var outcome = await workflow.ImportAsync(
            99999, [CreateSourceFile("dune.epub")], FileAction.Copy, CancellationToken.None);

        Assert.False(outcome.Success);
        Assert.Contains(outcome.Errors, e => e.Contains("not found", StringComparison.OrdinalIgnoreCase));
    }
}
