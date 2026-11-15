/* Bookmarkarr is licensed under the GNU AGPL v3 or later. */
using Bookmarkarr.Domain.Common;
using Bookmarkarr.Domain.Search;
using Bookmarkarr.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bookmarkarr.Infrastructure.Downloads.Processing;

public partial class DownloadProcessingJobProcessor
{
    /// <summary>Block reason on a download held because it looks like several books.</summary>
    public const string MultiBookBlockReason = "MultiBookDownload";

    /// <summary>Set when a person retries a held download: bring it in as the one book after all.</summary>
    public const string MultiBookAllowedMetadataKey = "MultiBookImportAllowed";

    /// <summary>
    /// Set at grab time on a download taken as a quality upgrade, so that its import — and only
    /// its import — removes the files it replaces.
    /// </summary>
    public const string ReplacesExistingFilesMetadataKey = "ReplacesExistingFiles";

    /// <summary>
    /// Holds a download that looks like several books instead of filing every file under one.
    /// </summary>
    /// <remarks>
    /// Import renames each file to the book's title and registers it to that book, so a pack
    /// grabbed for one title became hundreds of files under it with their own names gone. The
    /// files are left where the client put them and the download waits for a person, who can
    /// place the books with Manual Import or retry the import to accept it as it is.
    ///
    /// The size test is only for grabs nobody chose. A person who picked a large release meant to.
    /// </remarks>
    private async Task<bool> TryHoldMultiBookDownloadAsync(
        IServiceScope scope, DownloadProcessingJob job, Download download, Audiobook audiobook,
        IReadOnlyList<string> files, IDownloadProcessingJobService jobService,
        IHistoryRepository historyRepository, string correlationId, CancellationToken ct)
    {
        if (string.Equals(download.GetMetadataString(MultiBookAllowedMetadataKey), bool.TrueString, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var audio = files.Where(FileUtils.IsAudioFile).ToList();
        var reason = MultiBookDownloadDetector.Detect(WantedBook.From(audiobook), download.Title, audio);
        if (reason == null && !download.IsManualGrab())
        {
            var profile = audiobook.QualityProfile
                ?? audiobook.Editions.FirstOrDefault(e => e.MediaType == EditionMediaType.Audiobook)?.QualityProfile;
            var ceiling = ReleaseSizeGuard.CeilingBytes(ebook: false, profile?.MaximumSize ?? 0);
            var total = audio.Sum(path => File.Exists(path) ? new FileInfo(path).Length : 0);
            if (total > ceiling)
            {
                reason = $"it holds {total / (1024 * 1024)} MB of audio, over the {ceiling / (1024 * 1024)} MB a single audiobook may be";
            }
        }

        if (reason == null)
        {
            return false;
        }

        var message = $"Held for review: {reason}. Importing it would file all {audio.Count} audio file(s) under '{audiobook.Title}'. " +
            "Place the books with Manual Import, or retry the import to bring it in as this one book.";
        job.AddLogEntry(message);
        var downloadService = scope.ServiceProvider.GetRequiredService<IDownloadService>();
        await downloadService.UpdateAsync(download.Blocked(MultiBookBlockReason, message));
        // Completed, not failed: a failed job is re-blocked with a generic reason by the caller.
        await jobService.UpdateJobAsync(job.MarkAsCompleted());
        await RecordHistoryAsync(historyRepository, download, audiobook, HistoryEvents.ImportFailed,
            HistoryOutcome.Failed, correlationId, message,
            new Dictionary<string, object> { ["JobId"] = job.Id, ["Reason"] = MultiBookBlockReason }, ct);
        logger.LogWarning("Download {DownloadId} held for review: {Reason}", download.Id, reason);
        return true;
    }

    /// <summary>
    /// After an upgrade lands, removes the audio files it replaces, on disk and in the library.
    /// </summary>
    /// <remarks>
    /// An upgrade used to be imported beside what was already there, so the folder held the book
    /// twice. Nothing is removed unless the download was grabbed as an upgrade and at least one of
    /// its audio files was imported; a file the new import wrote over, or wrote again, is kept.
    /// </remarks>
    private async Task RemoveSupersededFilesAsync(
        IServiceScope scope, DownloadProcessingJob job, Download download, Audiobook audiobook,
        BookmarkarrDbContext editionDb, List<BookEdition> editions, List<ImportResult> results, CancellationToken ct)
    {
        if (!string.Equals(download.GetMetadataString(ReplacesExistingFilesMetadataKey), bool.TrueString, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var imported = results
            .Where(r => r.Success && !string.IsNullOrWhiteSpace(r.FinalPath) && FileUtils.IsAudioFile(r.FinalPath!))
            .Select(r => Path.GetFullPath(r.FinalPath!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (imported.Count == 0)
        {
            return;
        }

        bool Superseded(string? path) =>
            !string.IsNullOrWhiteSpace(path) && FileUtils.IsAudioFile(path) && !imported.Contains(Path.GetFullPath(path));

        var fileRepository = scope.ServiceProvider.GetRequiredService<IAudiobookFileRepository>();
        var oldRecords = (await fileRepository.GetByAudiobookIdAsync(audiobook.Id, ct)).Where(f => Superseded(f.Path)).ToList();
        var edition = editions.FirstOrDefault(e => e.MediaType == EditionMediaType.Audiobook);
        var oldEditionFiles = edition?.Files.Where(f => Superseded(f.Path)).ToList() ?? [];

        foreach (var path in oldRecords.Select(f => f.Path!).Concat(oldEditionFiles.Select(f => f.Path))
                     .Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                job.AddLogEntry($"Removed '{Path.GetFileName(path)}', replaced by the upgrade");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The record goes regardless: the upgrade is the book now, and a stale file the
                // library no longer points at is found by the next reconciliation.
                logger.LogWarning(ex, "Could not remove superseded file {Path} for audiobook {AudiobookId}", path, audiobook.Id);
            }
        }

        foreach (var record in oldRecords)
        {
            await fileRepository.DeleteAsync(record.Id, ct);
        }

        if (edition != null && oldEditionFiles.Count > 0)
        {
            foreach (var file in oldEditionFiles)
            {
                edition.Files.Remove(file);
            }

            await editionDb.SaveChangesAsync(ct);
        }

        if (oldRecords.Count + oldEditionFiles.Count > 0)
        {
            logger.LogInformation(
                "Upgrade of audiobook {AudiobookId} replaced {Count} earlier audio file(s)", audiobook.Id, Math.Max(oldRecords.Count, oldEditionFiles.Count));
        }
    }
}
