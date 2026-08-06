/* Bookmarkarr is licensed under the GNU AGPL v3 or later. */
using Microsoft.Extensions.Logging;

namespace Bookmarkarr.Application.Downloads.Submission;

public partial class DownloadService
{
    // Server-side mirror of the download state. The Wanted UI prefers live download
    // records; this is the fallback that survives a reload before those records arrive.
    private static EditionWantedStatus? ToEditionStatus(DownloadStatus status) => status switch
    {
        DownloadStatus.Queued => EditionWantedStatus.Queued,
        DownloadStatus.Downloading => EditionWantedStatus.Downloading,
        DownloadStatus.Paused => EditionWantedStatus.Downloading,
        DownloadStatus.Processing => EditionWantedStatus.Downloading,
        DownloadStatus.Ready => EditionWantedStatus.Downloading,
        DownloadStatus.ImportPending => EditionWantedStatus.Downloading,
        DownloadStatus.ImportBlocked => EditionWantedStatus.ImportBlocked,
        DownloadStatus.Failed => EditionWantedStatus.Failed,
        _ => null
    };

    private static SearchResult ToSearchResult(
        TrustedDownloadCandidate candidate,
        PreparedDownloadSubmission prepared) => new()
    {
        Id = candidate.Id,
        Title = candidate.Title,
        Artist = candidate.Artist,
        Album = candidate.Album,
        Source = candidate.Source,
        Quality = candidate.Quality,
        Language = candidate.Language,
        Size = candidate.Size,
        Seeders = candidate.Seeders,
        DownloadType = prepared.Protocol.ToString()
    };

    private async Task LogDownloadHistory(Audiobook audiobook, string source, SearchResult result)
    {
        try
        {
            logger.LogInformation("LogDownloadHistory: audiobook={Title}, source={Source}, result={ResultTitle}", audiobook?.Title, source, result?.Title);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException or StackOverflowException))
        {
            System.Diagnostics.Debug.WriteLine("Suppressed non-fatal exception in catch block.");
        }
        await Task.CompletedTask;
    }

    public async Task UpdateAsync(Download download)
    {
        var previous = await downloadRepository.GetByIdAsync(download.Id);
        if (previous is null)
        {
            logger.LogWarning("Skipping update for unknown download {DownloadId}", LogRedaction.SanitizeText(download.Id));
            return;
        }

        await downloadRepository.UpdateAsync(download);
        await SyncEditionStatusAsync(download);
        switch (previous.Status, download.Status)
        {
            case var (old, next) when old == next:
                return;
            case (_, DownloadStatus.Moved):
                await notificationService.OnDownloadImportedAsync(download);
                return;
            case (_, DownloadStatus.Failed):
                // A true download failure — the client could not fetch the bytes. This is the
                // only transition that counts a strike toward the blocklist.
                if (await blocklistService.RecordFailureAsync(download) is not null)
                {
                    await MakeEligibleForImmediateSearchAsync(download);
                }

                await notificationService.OnDownloadFailedAsync(download);
                return;
            case (_, DownloadStatus.ImportBlocked):
                // The bytes arrived and Bookmarkarr could not file them. The release is fine;
                // blocklisting here would discard a good grab because of a fault on our side.
                await notificationService.OnDownloadFailedAsync(download);
                return;
        }
    }

    /// <summary>
    /// Clears the book's last-search timestamp so the next automatic cycle searches it again
    /// instead of waiting out the remainder of its six-hour window.
    ///
    /// Search now skips the release that was just blocklisted, so the re-search picks the next
    /// best candidate. That is what makes the process terminate rather than spin: each pass
    /// either grabs a different release or finds nothing left to try.
    /// </summary>
    private async Task MakeEligibleForImmediateSearchAsync(Download download)
    {
        if (download.AudiobookId is not int audiobookId || audiobookId <= 0)
        {
            return;
        }

        var audiobook = await audiobookRepository.GetByIdAsync(audiobookId);
        if (audiobook is null)
        {
            return;
        }

        audiobook.LastSearchTime = null;
        await audiobookRepository.UpdateAsync(audiobook);

        logger.LogInformation(
            "Audiobook {AudiobookId} is eligible for immediate re-search after a release was blocklisted",
            audiobookId);
    }

    private async Task SyncEditionStatusAsync(Download download)
    {
        if (download.AudiobookId is not int audiobookId || audiobookId <= 0)
        {
            return;
        }

        var targetStatus = ToEditionStatus(download.Status);
        if (targetStatus is null)
        {
            return;
        }

        var audiobook = await audiobookRepository.GetByIdAsync(audiobookId);
        if (audiobook?.Editions == null || audiobook.Editions.Count == 0)
        {
            return;
        }

        BookEdition? edition;
        if (download.EditionId.HasValue)
        {
            edition = audiobook.Editions.FirstOrDefault(item => item.Id == download.EditionId.Value);
        }
        else
        {
            // Untargeted record: only attribute it when there is exactly one audiobook
            // edition it could belong to. Guessing would mark the wrong edition.
            var audiobookEditions = audiobook.Editions
                .Where(item => item.MediaType == EditionMediaType.Audiobook)
                .ToList();
            edition = audiobookEditions.Count == 1 ? audiobookEditions[0] : null;
        }

        if (edition == null)
        {
            return;
        }

        // A completed import wins over a late failure notice: if files already landed the
        // edition is genuinely available, whatever the download record ended up saying.
        edition.Status = download.Status is DownloadStatus.Failed or DownloadStatus.ImportBlocked
            ? edition.Files.Count > 0 ? EditionWantedStatus.Imported : targetStatus.Value
            : targetStatus.Value;
        edition.UpdatedAt = DateTime.UtcNow;
        await audiobookRepository.SaveChangesAsync();
    }
}
