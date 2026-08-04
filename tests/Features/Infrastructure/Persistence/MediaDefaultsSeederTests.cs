/* Bookmarkarr is licensed under the GNU AGPL v3 or later. */
using Bookmarkarr.Domain.Books;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bookmarkarr.Tests.Features.Infrastructure.Persistence;

public sealed class MediaDefaultsSeederTests : IAsyncLifetime
{
    private readonly string _databasePath =
        Path.Join(Path.GetTempPath(), "bookmarkarr-tests", $"media-defaults-{Guid.NewGuid():N}.db");
    private readonly string _rootDirectory =
        Path.Join(Path.GetTempPath(), "bookmarkarr-tests", $"roots-{Guid.NewGuid():N}");

    private ServiceProvider _provider = null!;
    private string _audiobookRoot = null!;
    private string _ebookRoot = null!;
    private string _downloadsRoot = null!;

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);

        // Real writable directories: the seeder deliberately probes for write access, so
        // pointing at nonexistent paths would exercise the skip path instead.
        _audiobookRoot = Path.Join(_rootDirectory, "audiobooks");
        _ebookRoot = Path.Join(_rootDirectory, "ebooks");
        _downloadsRoot = Path.Join(_rootDirectory, "downloads");
        Directory.CreateDirectory(_audiobookRoot);
        Directory.CreateDirectory(_ebookRoot);
        Directory.CreateDirectory(_downloadsRoot);

        Environment.SetEnvironmentVariable("BOOKMARKARR_AUDIOBOOK_ROOT", _audiobookRoot);
        Environment.SetEnvironmentVariable("BOOKMARKARR_EBOOK_ROOT", _ebookRoot);
        Environment.SetEnvironmentVariable("BOOKMARKARR_DOWNLOADS_ROOT", _downloadsRoot);

        var services = new ServiceCollection();
        services.AddDbContextFactory<BookmarkarrDbContext>(options =>
            options.UseSqlite($"Data Source={_databasePath};Pooling=False"));
        services.AddLogging();
        services.AddSingleton(Mock.Of<IConfigurationService>());
        services.AddSingleton<IFileNamingService, FileNamingService>();
        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BookmarkarrDbContext>>();
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        Environment.SetEnvironmentVariable("BOOKMARKARR_AUDIOBOOK_ROOT", null);
        Environment.SetEnvironmentVariable("BOOKMARKARR_EBOOK_ROOT", null);
        Environment.SetEnvironmentVariable("BOOKMARKARR_DOWNLOADS_ROOT", null);

        await _provider.DisposeAsync();
        if (File.Exists(_databasePath)) File.Delete(_databasePath);
        if (Directory.Exists(_rootDirectory)) Directory.Delete(_rootDirectory, recursive: true);
    }

    private Task RunSeederAsync() =>
        new MediaDefaultsSeeder(_provider, NullLogger<MediaDefaultsSeeder>.Instance)
            .StartAsync(CancellationToken.None);

    private BookmarkarrDbContext CreateContext() =>
        _provider.GetRequiredService<IDbContextFactory<BookmarkarrDbContext>>().CreateDbContext();

    [Fact]
    public async Task FreshInstall_SeedsSeparateAudiobookAndEbookDefaults()
    {
        await RunSeederAsync();

        await using var db = CreateContext();

        var audiobookProfile = await db.QualityProfiles
            .SingleAsync(p => p.IsDefault && p.MediaType == EditionMediaType.Audiobook);
        var ebookProfile = await db.QualityProfiles
            .SingleAsync(p => p.IsDefault && p.MediaType == EditionMediaType.Ebook);

        Assert.Equal("Default Audiobook", audiobookProfile.Name);
        Assert.Equal("Default Ebook", ebookProfile.Name);

        // Ebook scoring must not reuse audio codec/bitrate rungs.
        Assert.Contains("epub", ebookProfile.PreferredFormats);
        Assert.DoesNotContain(ebookProfile.Qualities, q => q.Quality.Contains("kbps"));
        Assert.Contains(audiobookProfile.Qualities, q => q.Quality.Contains("kbps"));

        var audiobookRoot = await db.RootFolders
            .SingleAsync(r => r.MediaType == EditionMediaType.Audiobook);
        var ebookRoot = await db.RootFolders
            .SingleAsync(r => r.MediaType == EditionMediaType.Ebook);
        Assert.Equal(_audiobookRoot, audiobookRoot.Path);
        Assert.Equal(_ebookRoot, ebookRoot.Path);
        Assert.True(audiobookRoot.IsDefault);
        Assert.True(ebookRoot.IsDefault);

        Assert.True(Directory.Exists(Path.Combine(_downloadsRoot, "audiobooks")));
        Assert.True(Directory.Exists(Path.Combine(_downloadsRoot, "ebooks")));
    }

    [Fact]
    public async Task RunningTwice_IsIdempotent()
    {
        await RunSeederAsync();
        await RunSeederAsync();

        await using var db = CreateContext();
        Assert.Equal(2, await db.QualityProfiles.CountAsync());
        Assert.Equal(2, await db.RootFolders.CountAsync());
    }

    [Fact]
    public async Task ExistingUserConfiguration_IsPreservedNotOverwritten()
    {
        await using (var seed = CreateContext())
        {
            seed.QualityProfiles.Add(new QualityProfile
            {
                Name = "My Audiobook Profile",
                MediaType = EditionMediaType.Audiobook,
                IsDefault = true,
                MinimumScore = 42
            });
            seed.RootFolders.Add(new RootFolder
            {
                Name = "My Library",
                Path = Path.Join(_rootDirectory, "custom-audio"),
                MediaType = EditionMediaType.Audiobook,
                IsDefault = true
            });
            await seed.SaveChangesAsync();
        }

        await RunSeederAsync();

        await using var db = CreateContext();

        // The user's audiobook choices survive untouched and are not duplicated.
        var audiobookProfiles = await db.QualityProfiles
            .Where(p => p.MediaType == EditionMediaType.Audiobook)
            .ToListAsync();
        var single = Assert.Single(audiobookProfiles);
        Assert.Equal("My Audiobook Profile", single.Name);
        Assert.Equal(42, single.MinimumScore);
        Assert.True(single.IsDefault);

        var audiobookRoots = await db.RootFolders
            .Where(r => r.MediaType == EditionMediaType.Audiobook)
            .ToListAsync();
        var singleRoot = Assert.Single(audiobookRoots);
        Assert.Equal("My Library", singleRoot.Name);

        // The missing ebook side is still filled in.
        Assert.True(await db.QualityProfiles
            .AnyAsync(p => p.IsDefault && p.MediaType == EditionMediaType.Ebook));
        Assert.True(await db.RootFolders
            .AnyAsync(r => r.IsDefault && r.MediaType == EditionMediaType.Ebook));
    }

    [Fact]
    public async Task ExistingProfileWithoutDefaultFlag_IsPromotedRatherThanDuplicated()
    {
        await using (var seed = CreateContext())
        {
            seed.QualityProfiles.Add(new QualityProfile
            {
                Name = "Lonely Ebook Profile",
                MediaType = EditionMediaType.Ebook,
                IsDefault = false
            });
            await seed.SaveChangesAsync();
        }

        await RunSeederAsync();

        await using var db = CreateContext();
        var ebookProfile = Assert.Single(
            await db.QualityProfiles.Where(p => p.MediaType == EditionMediaType.Ebook).ToListAsync());
        Assert.Equal("Lonely Ebook Profile", ebookProfile.Name);
        Assert.True(ebookProfile.IsDefault);
    }

    [Fact]
    public async Task EditionsMissingDefaults_AreRepairedWithTheirOwnMediaDefaults()
    {
        int audiobookEditionId;
        int ebookEditionId;

        await using (var seed = CreateContext())
        {
            var book = new Audiobook { Title = "Repair Me", Authors = ["Someone"], Editions = [] };
            seed.Audiobooks.Add(book);
            await seed.SaveChangesAsync();

            var audiobookEdition = BookEdition.Create(book.Id, EditionMediaType.Audiobook);
            var ebookEdition = BookEdition.Create(book.Id, EditionMediaType.Ebook);
            seed.BookEditions.AddRange(audiobookEdition, ebookEdition);
            await seed.SaveChangesAsync();

            audiobookEditionId = audiobookEdition.Id;
            ebookEditionId = ebookEdition.Id;
        }

        await RunSeederAsync();

        await using var db = CreateContext();
        var profiles = await db.QualityProfiles.Where(p => p.IsDefault)
            .ToDictionaryAsync(p => p.MediaType);

        var repairedAudiobook = await db.BookEditions.SingleAsync(e => e.Id == audiobookEditionId);
        var repairedEbook = await db.BookEditions.SingleAsync(e => e.Id == ebookEditionId);

        Assert.Equal(profiles[EditionMediaType.Audiobook].Id, repairedAudiobook.QualityProfileId);

        // The whole point: the ebook edition gets the ebook profile, never the audiobook one.
        Assert.Equal(profiles[EditionMediaType.Ebook].Id, repairedEbook.QualityProfileId);
        Assert.NotEqual(profiles[EditionMediaType.Audiobook].Id, repairedEbook.QualityProfileId);
    }

    [Fact]
    public async Task BooksMissingTheLegacyProfileColumn_AreRepairedFromTheirEdition()
    {
        // Regression: repairing only editions left the parent book's profile null, and
        // automatic search reads the book-level column - so every one of those books
        // failed with "no quality profile assigned" despite a correctly set up edition.
        int bookId;
        await using (var seed = CreateContext())
        {
            var book = new Audiobook { Title = "Legacy Book", Authors = ["Someone"], Editions = [] };
            seed.Audiobooks.Add(book);
            await seed.SaveChangesAsync();
            bookId = book.Id;

            seed.BookEditions.Add(BookEdition.Create(book.Id, EditionMediaType.Audiobook));
            await seed.SaveChangesAsync();
        }

        await RunSeederAsync();

        await using var db = CreateContext();
        var repaired = await db.Audiobooks.SingleAsync(b => b.Id == bookId);
        var edition = await db.BookEditions
            .SingleAsync(e => e.BookId == bookId && e.MediaType == EditionMediaType.Audiobook);

        Assert.NotNull(repaired.QualityProfileId);

        // The book must agree with its edition rather than being guessed separately.
        Assert.Equal(edition.QualityProfileId, repaired.QualityProfileId);
    }

    [Fact]
    public async Task BooksThatAlreadyHaveAProfile_AreLeftAlone()
    {
        int bookId;
        int deliberateProfileId;
        await using (var seed = CreateContext())
        {
            var profile = new QualityProfile { Name = "Hand Picked", MediaType = EditionMediaType.Audiobook };
            seed.QualityProfiles.Add(profile);
            await seed.SaveChangesAsync();
            deliberateProfileId = profile.Id;

            var book = new Audiobook
            {
                Title = "Already Configured",
                Authors = ["Someone"],
                QualityProfileId = profile.Id,
                Editions = []
            };
            seed.Audiobooks.Add(book);
            await seed.SaveChangesAsync();
            bookId = book.Id;
        }

        await RunSeederAsync();

        await using var db = CreateContext();
        var untouched = await db.Audiobooks.SingleAsync(b => b.Id == bookId);
        Assert.Equal(deliberateProfileId, untouched.QualityProfileId);
    }

    [Fact]
    public async Task BooksMissingBasePath_AreDerivedFromTheirAudiobookEditionRoot()
    {
        int bookId;
        await using (var seed = CreateContext())
        {
            seed.ApplicationSettings.Add(new ApplicationSettings
            {
                FolderNamingPattern = "{Author}/{Series}/{Title}"
            });
            var book = new Audiobook
            {
                Title = "A Summoner Awakens: Origins: (A Deck Building LitRPG)",
                Authors = ["Kerberos"],
                Series = "A Summoner Awakens",
                Editions = []
            };
            seed.Audiobooks.Add(book);
            await seed.SaveChangesAsync();
            bookId = book.Id;

            seed.BookEditions.Add(new BookEdition
            {
                BookId = book.Id,
                MediaType = EditionMediaType.Audiobook,
                RootPath = _audiobookRoot
            });
            await seed.SaveChangesAsync();
        }

        await RunSeederAsync();

        await using var db = CreateContext();
        var repaired = await db.Audiobooks.SingleAsync(book => book.Id == bookId);
        Assert.Equal(
            Path.Join(
                _audiobookRoot,
                "Kerberos",
                "A Summoner Awakens",
                "A Summoner Awakens_ Origins_ (A Deck Building LitRPG)"),
            repaired.BasePath);
    }

    [Fact]
    public async Task BooksWithExistingBasePath_AreNotRepointed()
    {
        var customPath = Path.Join(_rootDirectory, "custom", "Keep This Path");
        int bookId;
        await using (var seed = CreateContext())
        {
            var book = new Audiobook
            {
                Title = "Configured Book",
                Authors = ["Someone"],
                BasePath = customPath,
                Editions = []
            };
            seed.Audiobooks.Add(book);
            await seed.SaveChangesAsync();
            bookId = book.Id;
            seed.BookEditions.Add(new BookEdition
            {
                BookId = book.Id,
                MediaType = EditionMediaType.Audiobook,
                RootPath = _audiobookRoot
            });
            await seed.SaveChangesAsync();
        }

        await RunSeederAsync();

        await using var db = CreateContext();
        var untouched = await db.Audiobooks.SingleAsync(book => book.Id == bookId);
        Assert.Equal(customPath, untouched.BasePath);
    }

    [Fact]
    public async Task MissingLibraryMount_IsSkippedWithoutRegisteringAnUnusableRoot()
    {
        Environment.SetEnvironmentVariable(
            "BOOKMARKARR_EBOOK_ROOT", Path.Join(_rootDirectory, "not-mounted"));

        await RunSeederAsync();

        await using var db = CreateContext();

        // A root we cannot write to is never registered; the audiobook side still works.
        Assert.False(await db.RootFolders.AnyAsync(r => r.MediaType == EditionMediaType.Ebook));
        Assert.True(await db.RootFolders.AnyAsync(r => r.MediaType == EditionMediaType.Audiobook));

        // Profiles are independent of mounts, so both are still seeded.
        Assert.True(await db.QualityProfiles
            .AnyAsync(p => p.IsDefault && p.MediaType == EditionMediaType.Ebook));
    }
}
