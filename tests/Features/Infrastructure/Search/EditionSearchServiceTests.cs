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
        search.Setup(s => s.SearchIndexersAsync(It.IsAny<string>(), "custom-ebooks", It.IsAny<SearchSortBy>(), It.IsAny<SearchSortDirection>(), true, It.Is<SearchRequest>(r => r.ContentType == SearchContentType.Ebook)))
            .ReturnsAsync(new List<IndexerSearchResult> { new() { Id = "ebook-release", Title = "Example EPUB", Category = "Ebooks", Format = "EPUB", Quality = "EPUB", DownloadType = "Usenet", NzbUrl = "https://example.invalid/download.nzb" } });
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
}
