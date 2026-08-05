/* Bookmarkarr is licensed under the GNU AGPL v3 or later. */
using Bookmarkarr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Bookmarkarr.Api.Features.Library;

public sealed class LibraryStatusReconciliationWorkflow(
    BookmarkarrDbContext db,
    IDownloadQueueService downloadQueueService,
    ILogger<LibraryStatusReconciliationWorkflow> logger)
{
    public async Task<LibraryStatusReconciliationResult> ReconcileAsync(
        bool dryRun,
        CancellationToken ct = default)
    {
        var snapshot = await downloadQueueService.GetQueueSnapshotAsync();
        var liveQueueDownloadIds = snapshot.Items
            .Where(IsLiveQueueItem)
            .Select(item => item.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var clientSnapshots = snapshot.Clients
            .Where(client => !string.IsNullOrWhiteSpace(client.ClientId))
            .GroupBy(client => client.ClientId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var books = await db.Audiobooks
            .Include(book => book.Files)
            .Include(book => book.Editions)
                .ThenInclude(edition => edition.Files)
            .ToListAsync(ct);
        var downloads = await db.Downloads.ToListAsync(ct);
        var plannedDownloadStatuses = new Dictionary<string, DownloadStatus>(StringComparer.OrdinalIgnoreCase);
        var changes = new List<LibraryStatusReconciliationChange>();
        var markedSourceMissing = 0;
        var restoredImportBlocked = 0;
        var skippedUnavailable = 0;
        var editionsMarkedMissing = 0;
        var editionsMarkedImported = 0;
        var editionsRestoredBlocked = 0;

        foreach (var book in books)
        {
            foreach (var edition in book.Editions)
            {
                var relatedDownloads = downloads
                    .Where(download => BelongsToEdition(download, edition))
                    .ToList();
                var sourceStateDownloads = relatedDownloads
                    .Where(download => ShouldReconcileSourceState(download.Status))
                    .ToList();
                var editionMarkedSourceMissing = 0;
                var editionRestoredBlocked = 0;

                foreach (var download in sourceStateDownloads)
                {
                    var sourceIsVisible = liveQueueDownloadIds.Contains(download.Id);
                    var clientIsAuthoritative = HasAuthoritativeSnapshot(download, clientSnapshots);

                    if (sourceIsVisible && download.Status == DownloadStatus.SourceMissing)
                    {
                        plannedDownloadStatuses[download.Id] = DownloadStatus.ImportBlocked;
                        editionRestoredBlocked++;
                        restoredImportBlocked++;
                    }
                    else if (!sourceIsVisible &&
                        clientIsAuthoritative &&
                        download.Status != DownloadStatus.SourceMissing)
                    {
                        plannedDownloadStatuses[download.Id] = DownloadStatus.SourceMissing;
                        editionMarkedSourceMissing++;
                        markedSourceMissing++;
                    }
                    else if (!sourceIsVisible &&
                        !clientIsAuthoritative &&
                        download.Status != DownloadStatus.SourceMissing)
                    {
                        skippedUnavailable++;
                    }
                }

                var originalEditionStatus = edition.Status;
                var desiredEditionStatus = ResolveEditionStatus(
                    book,
                    edition,
                    relatedDownloads,
                    plannedDownloadStatuses,
                    liveQueueDownloadIds);

                if (desiredEditionStatus != originalEditionStatus)
                {
                    if (desiredEditionStatus == EditionWantedStatus.Missing) editionsMarkedMissing++;
                    if (desiredEditionStatus == EditionWantedStatus.Imported) editionsMarkedImported++;
                    if (desiredEditionStatus == EditionWantedStatus.ImportBlocked) editionsRestoredBlocked++;
                }

                if (desiredEditionStatus != originalEditionStatus ||
                    editionMarkedSourceMissing > 0 ||
                    editionRestoredBlocked > 0)
                {
                    changes.Add(new LibraryStatusReconciliationChange(
                        book.Id,
                        edition.Id,
                        edition.MediaType,
                        originalEditionStatus,
                        desiredEditionStatus,
                        editionMarkedSourceMissing,
                        editionRestoredBlocked));
                }

                if (!dryRun && desiredEditionStatus != originalEditionStatus)
                {
                    edition.Status = desiredEditionStatus;
                    edition.UpdatedAt = DateTime.UtcNow;
                }
            }
        }

        if (!dryRun)
        {
            ApplyDownloadChanges(downloads, plannedDownloadStatuses);
            await db.SaveChangesAsync(ct);
        }

        logger.LogInformation(
            "Library status reconciliation {Mode}: {EditionChanges} edition change(s), " +
            "{MissingSources} source-missing download(s), {RestoredSources} restored source(s), " +
            "{SkippedUnavailable} blocked download(s) skipped because client state was unavailable",
            dryRun ? "dry run" : "commit",
            changes.Count(change => change.FromStatus != change.ToStatus),
            markedSourceMissing,
            restoredImportBlocked,
            skippedUnavailable);

        return new LibraryStatusReconciliationResult(
            dryRun,
            books.Sum(book => book.Editions.Count),
            editionsMarkedMissing,
            editionsMarkedImported,
            editionsRestoredBlocked,
            markedSourceMissing,
            restoredImportBlocked,
            skippedUnavailable,
            snapshot.Clients.Count(client => !client.IsUnavailable && !client.IsStaleSnapshot),
            changes);
    }

    private static bool IsLiveQueueItem(QueueItem item) =>
        !item.IsStaleSnapshot &&
        !string.Equals(item.SnapshotState, "cached", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(item.SnapshotState, "unavailable", StringComparison.OrdinalIgnoreCase);

    private static bool HasAuthoritativeSnapshot(
        Download download,
        IReadOnlyDictionary<string, QueueClientStatus> clientSnapshots)
    {
        if (string.IsNullOrWhiteSpace(download.DownloadClientId) ||
            !clientSnapshots.TryGetValue(download.DownloadClientId, out var client))
        {
            return false;
        }

        return !client.IsUnavailable &&
            !client.IsStaleSnapshot &&
            string.Equals(client.SnapshotState, "live", StringComparison.OrdinalIgnoreCase);
    }

    private static bool BelongsToEdition(Download download, BookEdition edition)
    {
        if (download.EditionId.HasValue)
        {
            return download.EditionId.Value == edition.Id;
        }

        return edition.MediaType == EditionMediaType.Audiobook &&
            download.AudiobookId == edition.BookId;
    }

    private static bool ShouldReconcileSourceState(DownloadStatus downloadStatus) => downloadStatus is
        DownloadStatus.ImportBlocked or
        DownloadStatus.SourceMissing or
        // A completed handoff whose source has vanished never imports on its own, whatever
        // the edition currently reads. Left behind it counts as an active download, which
        // pins the edition to Downloading and suppresses automatic search indefinitely.
        DownloadStatus.Completed or
        DownloadStatus.Ready or
        DownloadStatus.ImportPending;

    private static EditionWantedStatus ResolveEditionStatus(
        Audiobook book,
        BookEdition edition,
        IReadOnlyCollection<Download> relatedDownloads,
        IReadOnlyDictionary<string, DownloadStatus> plannedDownloadStatuses,
        IReadOnlySet<string> liveQueueDownloadIds)
    {
        if (!edition.Monitored)
        {
            return EditionWantedStatus.Unmonitored;
        }

        var hasFiles = edition.Files.Count > 0 ||
            (edition.MediaType == EditionMediaType.Audiobook && book.Files?.Count > 0);
        if (hasFiles)
        {
            // An import the client is still exposing owns the edition status until it lands.
            // Claiming Imported underneath it only fights DownloadService, which mirrors the
            // in-flight download straight back onto the edition.
            var hasLiveImport = relatedDownloads.Any(download =>
                liveQueueDownloadIds.Contains(download.Id) &&
                IsInFlight(plannedDownloadStatuses.GetValueOrDefault(download.Id, download.Status)));
            return hasLiveImport ? edition.Status : EditionWantedStatus.Imported;
        }

        var effectiveStatuses = relatedDownloads
            .Select(download => plannedDownloadStatuses.GetValueOrDefault(download.Id, download.Status))
            .ToList();
        if (effectiveStatuses.Contains(DownloadStatus.ImportBlocked))
        {
            return EditionWantedStatus.ImportBlocked;
        }

        if (effectiveStatuses.Any(IsInFlight))
        {
            return edition.Status;
        }

        // Nothing is in flight and nothing landed, so any state implying otherwise is stale.
        // Wanted states must fall back to Missing or the edition is never searched again.
        return edition.Status is
            EditionWantedStatus.ImportBlocked or
            EditionWantedStatus.Imported or
            EditionWantedStatus.Downloading or
            EditionWantedStatus.Queued
            ? EditionWantedStatus.Missing
            : edition.Status;
    }

    private static bool IsInFlight(DownloadStatus status) => status is
        DownloadStatus.Queued or
        DownloadStatus.Downloading or
        DownloadStatus.Paused or
        DownloadStatus.Completed or
        DownloadStatus.Processing or
        DownloadStatus.Ready or
        DownloadStatus.ImportPending;

    private static void ApplyDownloadChanges(
        IEnumerable<Download> downloads,
        IReadOnlyDictionary<string, DownloadStatus> plannedStatuses)
    {
        foreach (var download in downloads)
        {
            if (!plannedStatuses.TryGetValue(download.Id, out var status) || status == download.Status)
            {
                continue;
            }

            download.SetStatus(status);
            download.ActiveAudiobookDeduplicationKey = null;
            if (status == DownloadStatus.SourceMissing)
            {
                download.ImportBlockReason = null;
                download.ImportBlockMessages = null;
                download.ErrorMessage = "Completed source is no longer available from the download client";
            }
            else
            {
                download.ImportBlockReason = "Source available; import retry required";
                download.ImportBlockMessages = ["The source reappeared in the download client and can be retried."];
                download.ErrorMessage = null;
            }
        }
    }
}

public sealed record LibraryStatusReconciliationResult(
    bool DryRun,
    int EditionsScanned,
    int EditionsMarkedMissing,
    int EditionsMarkedImported,
    int EditionsRestoredImportBlocked,
    int DownloadsMarkedSourceMissing,
    int DownloadsRestoredImportBlocked,
    int DownloadsSkippedUnavailable,
    int HealthyClientSnapshots,
    List<LibraryStatusReconciliationChange> Changes);

public sealed record LibraryStatusReconciliationChange(
    int BookId,
    int EditionId,
    EditionMediaType MediaType,
    EditionWantedStatus FromStatus,
    EditionWantedStatus ToStatus,
    int DownloadsMarkedSourceMissing,
    int DownloadsRestoredImportBlocked);
