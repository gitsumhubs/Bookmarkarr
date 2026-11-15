/* Bookmarkarr is licensed under the GNU AGPL v3 or later. */
using Bookmarkarr.Infrastructure.Persistence;
using Bookmarkarr.Application.Audiobooks.Matching;
using Bookmarkarr.Application.Search.Filters;
using Bookmarkarr.Application.Search.Scoring;
using Bookmarkarr.Domain.Common;
using Bookmarkarr.Domain.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bookmarkarr.Infrastructure.HostedServices.Search;

/// <summary>
/// The one unattended search-and-grab. Every caller that grabs without a person choosing the
/// release comes through here: the automatic pass, the Wanted page's Search button, a retry after a
/// failed download, and search-after-import.
/// </summary>
/// <remarks>
/// Usenet is asked first, because it does not ban: a book found there costs the torrent site
/// nothing. Results are held against the wanted book before they are scored, since scoring ranks
/// format and seeders and never asked whether a release was the book at all. An indexer that could
/// not be asked leaves the search unfinished rather than recording the book as searched.
/// </remarks>
public sealed class EditionSearchService(
    BookmarkarrDbContext db, ISearchService search, IQualityProfileService profiles,
    IDownloadService downloads, IDownloadRepository downloadRepository,
    IAudiobookFileRepository fileRepository, IConfigurationService configuration,
    IFileSystem fileSystem, IFileNamingService naming, IBlocklistService blocklist,
    ISearchThrottle throttle, IIndexerRepository indexers, ILogger<EditionSearchService> logger) : IEditionSearchService
{
    public async Task<SearchAndDownloadResult> SearchAndDownloadForBookAsync(int bookId, CancellationToken ct = default)
    {
        var editionId = await db.BookEditions.AsNoTracking()
            .Where(e => e.BookId == bookId && e.MediaType == EditionMediaType.Audiobook)
            .Select(e => (int?)e.Id).FirstOrDefaultAsync(ct);
        return editionId is null
            ? Result("This book has no audiobook edition to search for.")
            : await SearchAndDownloadAsync(editionId.Value, ct);
    }

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

        var (hasFiles, best) = await DescribeExistingFilesAsync(edition, profile, ct);
        if (hasFiles && (!edition.UpgradeAllowed || best is null || QualityMatcher.LabelMeetsCutoff(best, profile)))
            return Result("This edition already has files and does not need an upgrade.");

        var enabled = await indexers.GetEnabledAsync(true, ct);
        var torrentActive = IndexerProtocol.AnyTorrent(enabled);
        using var lease = await throttle.AcquireBookAsync(edition.Book.Title ?? "Edition search", torrentActive, ct);
        var isEbook = edition.MediaType == EditionMediaType.Ebook;
        var contentType = isEbook ? SearchContentType.Ebook : SearchContentType.Audiobook;
        var blocked = await blocklist.GetForAudiobookAsync(edition.BookId, ct);
        var report = new EditionSearchReport();
        QualityScore? top = null;

        // Usenet first. It does not ban, so a book found there never touches the torrent site.
        var phases = new List<IndexerProtocolFilter>();
        if (enabled.Any(indexer => !IndexerProtocol.IsTorrent(indexer))) phases.Add(IndexerProtocolFilter.Usenet);
        if (torrentActive) phases.Add(IndexerProtocolFilter.Torrent);
        if (phases.Count == 0) phases.Add(IndexerProtocolFilter.Any);

        foreach (var phase in phases)
        {
            foreach (var query in DownloadSearchQueryBuilder.BuildCandidates(edition.Book))
            {
                ct.ThrowIfCancellationRequested();
                var request = new SearchRequest { Query = query, ContentType = contentType, IncludeEnrichment = false };
                var outcome = await search.SearchIndexersWithOutcomeAsync(query, edition.DownloadCategory, isAutomaticSearch: true, request: request, protocol: phase);
                report.Unavailable.AddRange(outcome.Unavailable);
                // Nobody could be asked: the remaining rungs would be refused the same way.
                if (outcome.NobodyAnswered) break;
                var releases = outcome.Results.Where(r => SearchContentTypeClassifier.IsAllowed(r, contentType)).Select(SearchResultConverters.ToSearchResult).ToList();
                if (releases.Count == 0) continue;

                // The first rung that returns anything decides, as it always did. What changed is
                // that its results now have to be the book before they are ranked.
                var selection = UnattendedReleaseFilter.Apply(edition.Book, isEbook, profile, releases);
                report.Found += releases.Count;
                report.Refused.AddRange(selection.Refused);
                foreach (var refusal in selection.Refused)
                    logger.LogInformation("Refused '{Release}' for '{Book}': {Reason}", LogRedaction.SanitizeText(refusal.Title), LogRedaction.SanitizeText(edition.Book.Title), refusal.Reason);
                var scored = await profiles.ScoreSearchResults(selection.Accepted, profile);
                var eligible = scored.Where(s => !s.IsRejected && s.TotalScore > 0 && s.TotalScore >= profile.MinimumScore)
                    .Where(s => !blocked.Any(b => b.Matches(s.SearchResult.Id, s.SearchResult.IndexerId, s.SearchResult.Title, s.SearchResult.Size)))
                    .Where(s => !hasFiles || QualityMatcher.IsLabelBetter(s.SearchResult.Quality ?? s.SearchResult.Format, best, profile))
                    .OrderByDescending(s => s.TotalScore).ToList();
                report.RuledOutByProfile += selection.Accepted.Count - eligible.Count;
                top = eligible.FirstOrDefault();
                break;
            }
            if (top is not null) break;
        }

        var now = DateTime.UtcNow;
        edition.UpdatedAt = now;
        if (top is null && report.Unavailable.Count > 0)
        {
            // Not recorded as searched and not counted as a miss: part of the search never
            // happened, so the book comes round again on the next pass.
            edition.LastSearchOutcome = EditionSearchReport.Trim(report.Describe());
            await db.SaveChangesAsync(ct);
            return Result(report.Describe());
        }

        edition.LastSearchTime = now;
        if (edition.MediaType == EditionMediaType.Audiobook) edition.Book.LastSearchTime = now;
        if (top is null)
        {
            edition.SearchMisses++;
            edition.NextSearchTime = now + SearchBackoff.AfterMisses(edition.SearchMisses);
            edition.LastSearchOutcome = EditionSearchReport.Trim(report.Describe());
            await db.SaveChangesAsync(ct);
            return Result(report.Describe());
        }

        edition.SearchMisses = 0;
        edition.NextSearchTime = null;
        edition.LastSearchOutcome = EditionSearchReport.Trim($"Grabbed: {top.SearchResult.Title}");
        await db.SaveChangesAsync(ct);
        top.SearchResult.Score = top.TotalScore;
        // The submission workflow chooses a protocol-compatible client and routes by media type.
        var id = await downloads.SendToDownloadClientAsync(top.SearchResult, audiobookId: edition.BookId, contentType: contentType);
        if (hasFiles && !isEbook && await downloadRepository.GetByIdAsync(id) is { } upgrade)
        {
            // Marked so that this download's import, and no other, removes the files it replaces.
            upgrade.SetMetadata(Bookmarkarr.Infrastructure.Downloads.Processing.DownloadProcessingJobProcessor.ReplacesExistingFilesMetadataKey, bool.TrueString);
            await downloadRepository.UpdateAsync(upgrade);
        }
        logger.LogInformation("Queued {MediaType} edition {EditionId} as download {DownloadId}", edition.MediaType, edition.Id, id);
        return new SearchAndDownloadResult { Success = true, Message = "Release sent to download client.", DownloadId = id, IndexerUsed = top.SearchResult.Source, SearchResult = top.SearchResult };
    }

    private async Task<(bool HasFiles, string? Best)> DescribeExistingFilesAsync(BookEdition edition, QualityProfile profile, CancellationToken ct)
    {
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
        return (hasFiles, best);
    }

    private static SearchAndDownloadResult Result(string message) => new() { Message = message };
}

/// <summary>What one edition's search found, kept so "nothing grabbed" comes with its reason.</summary>
internal sealed class EditionSearchReport
{
    public int Found { get; set; }
    public int RuledOutByProfile { get; set; }
    public List<ReleaseRefusal> Refused { get; } = [];
    public List<IndexerUnavailability> Unavailable { get; } = [];

    public static string Trim(string text) => text.Length > 500 ? text[..500] : text;

    public string Describe()
    {
        var parts = new List<string>();
        if (Found == 0) parts.Add("No releases found.");
        else
        {
            parts.Add($"Found {Found} release(s), none acceptable.");
            if (Refused.Count > 0) parts.Add($"Not this book: {UnattendedReleaseFilter.Summarize(Refused)}.");
            if (RuledOutByProfile > 0) parts.Add($"{RuledOutByProfile} ruled out by the quality profile or blocklist.");
        }

        if (Unavailable.Count > 0)
        {
            var names = string.Join(", ", Unavailable.Select(u => u.IndexerName).Distinct());
            parts.Add($"Search unfinished: {names} could not be asked ({Unavailable[0].Reason}) It will be tried again.");
        }

        return string.Join(" ", parts);
    }
}
