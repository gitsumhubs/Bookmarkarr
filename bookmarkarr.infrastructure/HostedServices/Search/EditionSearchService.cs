/* Bookmarkarr is licensed under the GNU AGPL v3 or later. */
using Bookmarkarr.Infrastructure.Persistence;
using Bookmarkarr.Application.Audiobooks.Matching;
using Bookmarkarr.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bookmarkarr.Infrastructure.HostedServices.Search;

public sealed class EditionSearchService(
    BookmarkarrDbContext db, ISearchService search, IQualityProfileService profiles,
    IDownloadService downloads, IDownloadRepository downloadRepository,
    IAudiobookFileRepository fileRepository, IConfigurationService configuration,
    IFileSystem fileSystem, IFileNamingService naming, IBlocklistService blocklist,
    ISearchThrottle throttle, IIndexerRepository indexers, ILogger<EditionSearchService> logger) : IEditionSearchService
{
    public async Task<SearchAndDownloadResult> SearchAndDownloadAsync(int editionId, CancellationToken ct = default)
    {
        var edition = await db.BookEditions.Include(e => e.Book).Include(e => e.QualityProfile)
            .Include(e => e.Files).SingleOrDefaultAsync(e => e.Id == editionId, ct);
        if (edition is null) return Result("Edition not found.");
        var profile = edition.QualityProfile;
        if (profile is null || profile.MediaType != edition.MediaType)
            return Result($"Assign a {edition.MediaType} quality profile before searching.");
        var active = await downloadRepository.GetByAudiobookIdAsync(edition.BookId, ct);
        if (active.Any(d => (d.EditionId == edition.Id || (!d.EditionId.HasValue && DownloadContentTypes.IsEbook(d) == (edition.MediaType == EditionMediaType.Ebook)))
            && d.Status is DownloadStatus.Queued or DownloadStatus.Downloading or DownloadStatus.Paused
                or DownloadStatus.Processing or DownloadStatus.Ready or DownloadStatus.ImportPending or DownloadStatus.Completed))
            return Result("This edition already has a download in progress.");

        var labels = new List<string>();
        var hasFiles = edition.Files.Count > 0;
        if (edition.MediaType == EditionMediaType.Audiobook)
        {
            var book = edition.Book.ForEditionSearch(edition);
            var settings = await configuration.GetApplicationSettingsAsync();
            var presence = new LibraryPresenceInspector(fileSystem, naming, logger).Inspect(book, settings);
            var files = await fileRepository.GetByAudiobookIdAsync(book.Id, ct);
            hasFiles |= presence.HasFiles || files.Count > 0 || !string.IsNullOrWhiteSpace(book.FilePath);
            labels.AddRange(files.Select(f => AudiobookQualityCutoffEvaluator.ResolveFileQualityLabel(f, profile)).OfType<string>());
            if (presence.BestQuality is not null) labels.Add(presence.BestQuality);
        }
        else
        {
            labels.AddRange(edition.Files.Select(f => Path.GetExtension(f.Path).TrimStart('.').ToUpperInvariant()));
        }
        var best = labels.OrderBy(label => profile.Qualities.FirstOrDefault(q => q.Allowed && q.Quality.Equals(label, StringComparison.OrdinalIgnoreCase))?.Priority ?? int.MaxValue).FirstOrDefault();
        if (hasFiles && (!edition.UpgradeAllowed || best is null || QualityMatcher.LabelMeetsCutoff(best, profile)))
            return Result("This edition already has files and does not need an upgrade.");

        var torrentActive = IndexerProtocol.AnyTorrent(await indexers.GetEnabledAsync(true, ct));
        using var lease = await throttle.AcquireBookAsync(edition.Book.Title ?? "Edition search", torrentActive, ct);
        var contentType = edition.MediaType == EditionMediaType.Ebook ? SearchContentType.Ebook : SearchContentType.Audiobook;
        var releases = new List<SearchResult>();
        foreach (var query in DownloadSearchQueryBuilder.BuildCandidates(edition.Book))
        {
            ct.ThrowIfCancellationRequested();
            var request = new SearchRequest { Query = query, ContentType = contentType, IncludeEnrichment = false };
            var found = await search.SearchIndexersAsync(query, edition.DownloadCategory, isAutomaticSearch: true, request: request);
            releases = found.Where(r => Bookmarkarr.Application.Search.Filters.SearchContentTypeClassifier.IsAllowed(r, contentType)).Select(SearchResultConverters.ToSearchResult).ToList();
            if (releases.Count > 0) break;
        }
        edition.LastSearchTime = DateTime.UtcNow;
        edition.UpdatedAt = DateTime.UtcNow;
        if (edition.MediaType == EditionMediaType.Audiobook) edition.Book.LastSearchTime = edition.LastSearchTime;
        await db.SaveChangesAsync(ct);
        var blocked = await blocklist.GetForAudiobookAsync(edition.BookId, ct);
        var scored = await profiles.ScoreSearchResults(releases, profile);
        var top = scored.Where(s => !s.IsRejected && s.TotalScore > 0 && s.TotalScore >= profile.MinimumScore)
            .Where(s => !blocked.Any(b => b.Matches(s.SearchResult.Id, s.SearchResult.IndexerId, s.SearchResult.Title, s.SearchResult.Size)))
            .Where(s => !hasFiles || QualityMatcher.IsLabelBetter(s.SearchResult.Quality ?? s.SearchResult.Format, best, profile))
            .OrderByDescending(s => s.TotalScore).FirstOrDefault();
        if (top is null) return Result("No acceptable releases found for this edition.");
        top.SearchResult.Score = top.TotalScore;
        // The submission workflow chooses a protocol-compatible client and routes by media type.
        var id = await downloads.SendToDownloadClientAsync(top.SearchResult, audiobookId: edition.BookId, contentType: contentType);
        logger.LogInformation("Queued {MediaType} edition {EditionId} as download {DownloadId}", edition.MediaType, edition.Id, id);
        return new SearchAndDownloadResult { Success = true, Message = "Release sent to download client.", DownloadId = id, SearchResult = top.SearchResult };
    }

    private static SearchAndDownloadResult Result(string message) => new() { Message = message };
}
