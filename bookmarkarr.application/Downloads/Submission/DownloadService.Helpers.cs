/* Bookmarkarr is licensed under the GNU AGPL v3 or later. */
using Microsoft.Extensions.Logging;

namespace Bookmarkarr.Application.Downloads.Submission;

public partial class DownloadService
{
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
        switch (previous.Status, download.Status)
        {
            case var (old, next) when old == next:
                return;
            case (_, DownloadStatus.Moved):
                await notificationService.OnDownloadImportedAsync(download);
                return;
            case (_, DownloadStatus.Failed):
            case (_, DownloadStatus.ImportBlocked):
                await notificationService.OnDownloadFailedAsync(download);
                return;
        }
    }
}
