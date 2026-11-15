/* Bookmarkarr is licensed under the GNU AGPL v3 or later. */
using Bookmarkarr.Domain.Books;
using Bookmarkarr.Infrastructure.HostedServices.Search;
using Bookmarkarr.Tests.Common;
using Bookmarkarr.Tests.Builders;
using Microsoft.EntityFrameworkCore;

namespace Bookmarkarr.Tests.Features.Infrastructure.Search;

public sealed class EditionSearchServiceTests : BaseTests
{
    [Fact]
    public async Task EbookSearchUsesOwnProfileAndCategory_DespiteAudioDownloadAndUnmonitoredParent()
    {
        var search = new Mock<ISearchService>();
        search.Setup(s => s.SearchIndexersWithOutcomeAsync(It.IsAny<string>(), "custom-ebooks", true, It.Is<SearchRequest>(r => r.ContentType == SearchContentType.Ebook), It.IsAny<IndexerProtocolFilter>()))
            .ReturnsAsync(new IndexerSearchOutcome([new() { Id = "ebook-release", Title = "Example EPUB", Category = "Ebooks", Format = "EPUB", Quality = "EPUB", DownloadType = "Usenet", NzbUrl = "https://example.invalid/download.nzb" }], [], 1));
        var downloads = new Mock<IDownloadService>();
        downloads.Setup(s => s.SendToDownloadClientAsync(It.IsAny<SearchResult>(), null, It.IsAny<int?>(), SearchContentType.Ebook)).ReturnsAsync("ebook-download");
        Init(builder => builder.WithSingleton(search.Object).WithSingleton(downloads.Object));
        var profile = await _qualityProfileRepository.AddAsync(new QualityProfile { Name = "Ebooks", MediaType = EditionMediaType.Ebook,
            Qualities = [new() { Quality = "EPUB", Allowed = true }], PreferredFormats = ["epub"] });
        var book = await _audiobookRepository.AddAsync(new Audiobook { Title = "Example", Monitored = false,
            Editions = [new() { MediaType = EditionMediaType.Ebook, Monitored = true, QualityProfileId = profile.Id, DownloadCategory = "custom-ebooks" }] });
        await _downloadRepository.AddAsync(new Download { Id = "audio-in-progress", AudiobookId = book.Id, Status = DownloadStatus.Downloading, Metadata = new() { ["ContentType"] = "audiobook" } });
        var result = await _provider.GetRequiredService<IEditionSearchService>().SearchAndDownloadAsync(book.Editions[0].Id);
        Assert.True(result.Success, result.Message);
        downloads.Verify(s => s.SendToDownloadClientAsync(It.Is<SearchResult>(r => r.Quality == "EPUB"), null, book.Id, SearchContentType.Ebook), Times.Once);
        Assert.False((await _audiobookRepository.GetByIdAsync(book.Id))!.Monitored);
        Assert.NotNull((await _provider.GetRequiredService<BookmarkarrDbContext>().BookEditions.FindAsync(book.Editions[0].Id))!.LastSearchTime);
    }

    [Fact]
    public async Task AutomaticSchedulerSelectsMonitoredEbookWithUnmonitoredParent()
    {
        var editionSearch = new Mock<IEditionSearchService>();
        editionSearch.Setup(s => s.SearchAndDownloadAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new SearchAndDownloadResult());
        Init(builder => builder.WithSingleton(editionSearch.Object));
        var profile = await _qualityProfileRepository.AddAsync(new QualityProfile { Name = "Ebooks", MediaType = EditionMediaType.Ebook });
        var book = await _audiobookRepository.AddAsync(new Audiobook { Title = "Ebook wanted", Monitored = false,
            Editions = [new() { MediaType = EditionMediaType.Ebook, Monitored = true, QualityProfileId = profile.Id }] });
        var processor = new AutomaticSearchProcessor(_provider.GetRequiredService<ILogger<AutomaticSearchProcessor>>(), _provider.GetRequiredService<IServiceScopeFactory>());
        await processor.RunCycleAsync(default);
        editionSearch.Verify(s => s.SearchAndDownloadAsync(book.Editions[0].Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EbookScoringUsesFormatsRatherThanAudioBitrates()
    {
        var scorer = new Bookmarkarr.Application.Search.Scoring.SearchResultScorer(null, _provider.GetRequiredService<ILogger<EditionSearchServiceTests>>());
        var profile = new QualityProfile { MediaType = EditionMediaType.Ebook, Qualities = [new() { Quality = "EPUB", Allowed = true }] };
        var result = new SearchResult { Title = "Example EPUB", Format = "EPUB", Quality = "MP3 320kbps", DownloadType = "Torrent" };
        var score = await scorer.Score(result, profile);
        Assert.False(score.IsRejected); Assert.Equal("EPUB", result.Quality);
        Assert.Contains("EbookFormat", score.ScoreBreakdown.Keys); Assert.DoesNotContain("Quality", score.ScoreBreakdown.Keys);
    }

    private const string Wanted = "Dungeon Crawler Carl (Dungeon Crawler Carl, #1)";
    private const string RightRelease = "Dungeon Crawler Carl - Matt Dinniman [M4B] [128 Kbps]";
    private const string Pack = "Dungeon Crawler Carl Books 1-8 - Matt Dinniman [M4B]";
    private const string OtherVolume = "This Inevitable Ruin: Dungeon Crawler Carl, Book 7 - Matt Dinniman [M4B] [64 Kbps]";

    private static IndexerSearchResult Release(string title, long megabytes = 800) => new()
    {
        Id = title, Title = title, Category = "Audiobooks", Format = "M4B", Quality = "M4B", DownloadType = "Usenet",
        NzbUrl = "https://example.invalid/download.nzb", Size = megabytes * 1024 * 1024
    };

    private static IndexerSearchOutcome Found(params IndexerSearchResult[] releases) => new([.. releases], [], 1);

    /// <summary>A monitored audiobook edition, every release scoring the same, and a download service that records what was sent.</summary>
    private async Task<(BookEdition Edition, Mock<IDownloadService> Downloads)> ArrangeAsync(Mock<ISearchService> search)
    {
        var downloads = new Mock<IDownloadService>();
        downloads.Setup(s => s.SendToDownloadClientAsync(It.IsAny<SearchResult>(), null, It.IsAny<int?>(), SearchContentType.Audiobook)).ReturnsAsync("queued");
        var scoring = new Mock<IQualityProfileService>();
        scoring.Setup(s => s.ScoreSearchResults(It.IsAny<List<SearchResult>>(), It.IsAny<QualityProfile>()))
            .ReturnsAsync((List<SearchResult> releases, QualityProfile _) => releases.Select(r => new QualityScore { SearchResult = r, TotalScore = 100 }).ToList());
        Init(builder => builder.WithSingleton(search.Object).WithSingleton(downloads.Object).WithSingleton(scoring.Object));
        var profile = await _qualityProfileRepository.AddAsync(new QualityProfile { Name = "Audio", MediaType = EditionMediaType.Audiobook, MaximumSize = 0,
            Qualities = [new() { Quality = "M4B", Allowed = true }] });
        var book = await _audiobookRepository.AddAsync(new Audiobook { Title = Wanted, Authors = ["Matt Dinniman"],
            Editions = [new() { MediaType = EditionMediaType.Audiobook, Monitored = true, QualityProfileId = profile.Id, DownloadCategory = "audiobooks" }] });
        return (book.Editions[0], downloads);
    }

    private static Mock<ISearchService> SearchReturning(IndexerSearchOutcome outcome)
    {
        var search = new Mock<ISearchService>();
        search.Setup(s => s.SearchIndexersWithOutcomeAsync(It.IsAny<string>(), It.IsAny<string?>(), true, It.IsAny<SearchRequest>(), It.IsAny<IndexerProtocolFilter>())).ReturnsAsync(outcome);
        return search;
    }

    private async Task<BookEdition> ReloadAsync(int editionId) =>
        (await _provider.GetRequiredService<BookmarkarrDbContext>().BookEditions.AsNoTracking().SingleAsync(e => e.Id == editionId));

    [Fact]
    public async Task AmongAPackAnotherVolumeAndTheBook_OnlyTheBookIsGrabbed()
    {
        var (edition, downloads) = await ArrangeAsync(SearchReturning(Found(Release(Pack, 4000), Release(OtherVolume), Release(RightRelease))));

        var result = await _provider.GetRequiredService<IEditionSearchService>().SearchAndDownloadAsync(edition.Id);

        Assert.True(result.Success, result.Message);
        downloads.Verify(s => s.SendToDownloadClientAsync(It.Is<SearchResult>(r => r.Title == RightRelease), null, edition.BookId, SearchContentType.Audiobook), Times.Once);
        downloads.Verify(s => s.SendToDownloadClientAsync(It.Is<SearchResult>(r => r.Title != RightRelease), null, It.IsAny<int?>(), It.IsAny<SearchContentType>()), Times.Never);
        var saved = await ReloadAsync(edition.Id);
        Assert.Equal(0, saved.SearchMisses);
        Assert.StartsWith("Grabbed:", saved.LastSearchOutcome);
    }

    [Fact]
    public async Task WhenNothingIsTheBook_NothingIsGrabbed_TheReasonIsKept_AndTheNextSearchWaits()
    {
        var (edition, downloads) = await ArrangeAsync(SearchReturning(Found(Release(Pack, 4000), Release(OtherVolume))));

        var result = await _provider.GetRequiredService<IEditionSearchService>().SearchAndDownloadAsync(edition.Id);

        Assert.False(result.Success);
        downloads.Verify(s => s.SendToDownloadClientAsync(It.IsAny<SearchResult>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<SearchContentType>()), Times.Never);
        var saved = await ReloadAsync(edition.Id);
        Assert.Equal(1, saved.SearchMisses);
        Assert.NotNull(saved.LastSearchTime);
        Assert.InRange(saved.NextSearchTime!.Value - DateTime.UtcNow, TimeSpan.FromHours(5.9), TimeSpan.FromHours(6.1));
        Assert.Contains("several books in one", saved.LastSearchOutcome);
        Assert.Contains("a different volume", saved.LastSearchOutcome);
    }

    [Fact]
    public async Task TheRightBookAtPackSize_IsRefusedWhenTheProfileSetsNoMaximum()
    {
        var (edition, downloads) = await ArrangeAsync(SearchReturning(Found(Release(RightRelease, 9000))));

        var result = await _provider.GetRequiredService<IEditionSearchService>().SearchAndDownloadAsync(edition.Id);

        Assert.False(result.Success);
        downloads.Verify(s => s.SendToDownloadClientAsync(It.IsAny<SearchResult>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<SearchContentType>()), Times.Never);
        Assert.Contains("the wrong size", (await ReloadAsync(edition.Id)).LastSearchOutcome);
    }

    [Fact]
    public async Task AnIndexerThatCouldNotBeAsked_StopsTheLadder_AndTheBookIsNotRecordedAsSearched()
    {
        var search = SearchReturning(new IndexerSearchOutcome([], [new IndexerUnavailability("AudioBook Bay", "AudioBook Bay has used its hourly request budget.", true, TimeSpan.FromMinutes(20))], 1));
        var (edition, _) = await ArrangeAsync(search);

        var result = await _provider.GetRequiredService<IEditionSearchService>().SearchAndDownloadAsync(edition.Id);

        Assert.False(result.Success);
        Assert.Contains("could not be asked", result.Message);
        // One rung, not the whole ladder: the rest would have been refused the same way.
        search.Verify(s => s.SearchIndexersWithOutcomeAsync(It.IsAny<string>(), It.IsAny<string?>(), true, It.IsAny<SearchRequest>(), It.IsAny<IndexerProtocolFilter>()), Times.Once);
        var saved = await ReloadAsync(edition.Id);
        Assert.Null(saved.LastSearchTime);
        Assert.Equal(0, saved.SearchMisses);
        Assert.Null(saved.NextSearchTime);
    }

    [Fact]
    public async Task UsenetIsAskedFirst_AndTheTorrentSiteIsLeftAloneWhenItHasTheBook()
    {
        var search = new Mock<ISearchService>();
        search.Setup(s => s.SearchIndexersWithOutcomeAsync(It.IsAny<string>(), It.IsAny<string?>(), true, It.IsAny<SearchRequest>(), IndexerProtocolFilter.Usenet)).ReturnsAsync(Found(Release(RightRelease)));
        var (edition, downloads) = await ArrangeAsync(search);
        await _indexerRepository.AddAsync(new Indexer { Name = "Usenet Indexer", Type = "Usenet", Implementation = "Newznab", Url = "https://usenet.invalid" });
        await _indexerRepository.AddAsync(new Indexer { Name = "Torrent Indexer", Type = "Torrent", Implementation = "Torznab", Url = "https://torrent.invalid" });

        var result = await _provider.GetRequiredService<IEditionSearchService>().SearchAndDownloadAsync(edition.Id);

        Assert.True(result.Success, result.Message);
        search.Verify(s => s.SearchIndexersWithOutcomeAsync(It.IsAny<string>(), It.IsAny<string?>(), true, It.IsAny<SearchRequest>(), IndexerProtocolFilter.Torrent), Times.Never);
        downloads.Verify(s => s.SendToDownloadClientAsync(It.Is<SearchResult>(r => r.Title == RightRelease), null, edition.BookId, SearchContentType.Audiobook), Times.Once);
    }

    [Fact]
    public async Task SearchingByBook_UsesItsAudiobookEdition()
    {
        var (edition, downloads) = await ArrangeAsync(SearchReturning(Found(Release(RightRelease))));

        var result = await _provider.GetRequiredService<IEditionSearchService>().SearchAndDownloadForBookAsync(edition.BookId);

        Assert.True(result.Success, result.Message);
        downloads.Verify(s => s.SendToDownloadClientAsync(It.IsAny<SearchResult>(), null, edition.BookId, SearchContentType.Audiobook), Times.Once);
    }

    [Fact]
    public async Task TheAutomaticPass_LeavesAnEditionAloneUntilItsBackoffEnds()
    {
        var editionSearch = new Mock<IEditionSearchService>();
        editionSearch.Setup(s => s.SearchAndDownloadAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new SearchAndDownloadResult());
        Init(builder => builder.WithSingleton(editionSearch.Object));
        var profile = await _qualityProfileRepository.AddAsync(new QualityProfile { Name = "Audio", MediaType = EditionMediaType.Audiobook });
        var waiting = await _audiobookRepository.AddAsync(new Audiobook { Title = "Still waiting",
            Editions = [new() { MediaType = EditionMediaType.Audiobook, Monitored = true, QualityProfileId = profile.Id, LastSearchTime = DateTime.UtcNow.AddHours(-10), SearchMisses = 3, NextSearchTime = DateTime.UtcNow.AddHours(14) }] });
        var due = await _audiobookRepository.AddAsync(new Audiobook { Title = "Due again",
            Editions = [new() { MediaType = EditionMediaType.Audiobook, Monitored = true, QualityProfileId = profile.Id, LastSearchTime = DateTime.UtcNow.AddHours(-30), SearchMisses = 3, NextSearchTime = DateTime.UtcNow.AddHours(-6) }] });

        await new AutomaticSearchProcessor(_provider.GetRequiredService<ILogger<AutomaticSearchProcessor>>(), _provider.GetRequiredService<IServiceScopeFactory>()).RunCycleAsync(default);

        editionSearch.Verify(s => s.SearchAndDownloadAsync(due.Editions[0].Id, It.IsAny<CancellationToken>()), Times.Once);
        editionSearch.Verify(s => s.SearchAndDownloadAsync(waiting.Editions[0].Id, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void ANewEdition_DoesNotSearchForUpgrades()
    {
        Assert.False(new BookEdition().UpgradeAllowed);
        Assert.False(BookEdition.Create(1, EditionMediaType.Audiobook).UpgradeAllowed);
    }
}
