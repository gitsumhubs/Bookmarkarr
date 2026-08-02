/* Bookmarkarr is licensed under the GNU AGPL v3 or later. */
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Bookmarkarr.Infrastructure.Downloads.Processing;

public partial class DownloadProcessingJobProcessor
{
    private static async Task CompleteImportAsync(
        IServiceProvider services, IScanQueueService scanQueueService, DownloadProcessingJob job,
        IDownloadProcessingJobService jobService, IHistoryRepository historyRepository,
        Download download, Audiobook audiobook, DownloadClientConfiguration? client,
        string correlationId, bool isDirectDownload, bool isEbook, CancellationToken ct)
    {
        if (!job.HasCheckpoint("ClientMarkedImported"))
        {
            if (isDirectDownload)
            {
                job.SetCheckpoint("ClientMarkedImported");
                await jobService.UpdateJobAsync(job);
            }
            else
            {
                var gateway = services.GetRequiredService<IDownloadClientGateway>();
                if (!await gateway.MarkItemAsImportedAsync(client!, download, ct))
                {
                    await ScheduleRetryAsync(job, jobService, historyRepository, download, audiobook,
                        correlationId, $"Unable to mark the item imported in client {client!.Id}", ct);
                    return;
                }
                job.SetCheckpoint("ClientMarkedImported");
                await jobService.UpdateJobAsync(job);
            }
        }

        if (!job.HasCheckpoint("ScanEnqueued"))
        {
            if (isEbook)
            {
                job.SetCheckpoint("ScanEnqueued", "not-required-for-ebook");
                job.AddLogEntry("Skipped audiobook library scan for ebook import");
                await jobService.UpdateJobAsync(job);
            }
            else
            {
                Guid scanJobId;
                try
                {
                    scanJobId = await scanQueueService.EnqueueScanAsync(
                        audiobook,
                        path: null,
                        correlationId: correlationId,
                        downloadId: download.Id);
                }
                catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException or StackOverflowException))
                {
                    await ScheduleRetryAsync(job, jobService, historyRepository, download, audiobook,
                        correlationId, $"Unable to enqueue the post-import library scan: {exception.Message}", ct);
                    return;
                }
                job.SetCheckpoint("ScanEnqueued", scanJobId.ToString());
                job.AddLogEntry($"Enqueued scan job {scanJobId} for audiobook {audiobook.Id}");
                await jobService.UpdateJobAsync(job);
                await RecordHistoryAsync(historyRepository, download, audiobook, HistoryEvents.ScanQueued,
                    HistoryOutcome.Succeeded, correlationId, $"Library scan {scanJobId} queued",
                    new Dictionary<string, object> { ["JobId"] = job.Id, ["ScanJobId"] = scanJobId }, ct);
            }
        }

        var finalization = services.GetRequiredService<IImportFinalizationService>();
        try
        {
            await finalization.FinalizeAsync(job.Id, download.Id, audiobook.Id,
                audiobook.Title ?? download.Title, client?.Id ?? download.DownloadClientId,
                correlationId, new Dictionary<string, object>
                {
                    ["JobId"] = job.Id,
                    ["ScanJobId"] = job.TryGetJobDataString("ScanEnqueuedDetail", out var scanId) ? scanId : string.Empty
                }, ct);
        }
        catch (InvalidOperationException exception)
        {
            await ScheduleRetryAsync(job, jobService, historyRepository, download, audiobook,
                correlationId, $"Unable to commit import finalization: {exception.Message}", ct);
        }
    }

    private static async Task ScheduleRetryAsync(
        DownloadProcessingJob job, IDownloadProcessingJobService jobService,
        IHistoryRepository historyRepository, Download download, Audiobook audiobook,
        string correlationId, string reason, CancellationToken ct)
    {
        job.ScheduleRetry(reason);
        await jobService.UpdateJobAsync(job);
        var exhausted = job.Status == ProcessingJobStatus.Failed;
        await RecordHistoryAsync(historyRepository, download, audiobook,
            exhausted ? HistoryEvents.ImportFailed : HistoryEvents.ImportRetry,
            exhausted ? HistoryOutcome.Failed : HistoryOutcome.Retrying, correlationId, reason,
            new Dictionary<string, object> { ["JobId"] = job.Id, ["RetryCount"] = job.RetryCount }, ct);
    }

    private static async Task FailImportAsync(
        DownloadProcessingJob job, IDownloadProcessingJobService jobService,
        IHistoryRepository historyRepository, Download download, Audiobook audiobook,
        string correlationId, string reason, CancellationToken ct)
    {
        await jobService.UpdateJobAsync(job.MarkAsFailed(reason));
        await RecordHistoryAsync(historyRepository, download, audiobook, HistoryEvents.ImportFailed,
            HistoryOutcome.Failed, correlationId, reason,
            new Dictionary<string, object> { ["JobId"] = job.Id, ["RetryCount"] = job.RetryCount }, ct);
    }

    private static Task RecordHistoryAsync(
        IHistoryRepository historyRepository, Download download, Audiobook audiobook,
        string eventType, HistoryOutcome outcome, string correlationId, string message,
        Dictionary<string, object> details, CancellationToken ct) =>
        historyRepository.AddAsync(new History
        {
            AudiobookId = audiobook.Id,
            EditionId = download.EditionId,
            MediaType = DownloadContentTypes.IsEbook(download) ? "ebook" : "audiobook",
            AudiobookTitle = audiobook.Title,
            SourceTitle = download.Title,
            DownloadId = download.Id.ToUpperInvariant(),
            DownloadClientId = download.DownloadClientId,
            EventType = eventType,
            Outcome = outcome,
            Source = "DownloadImport",
            Message = message,
            Error = outcome == HistoryOutcome.Failed ? message : null,
            Timestamp = DateTime.UtcNow,
            CorrelationId = correlationId,
            Data = JsonSerializer.Serialize(details)
        }, ct);
}
