using Bookmarkarr.Infrastructure.Downloads.Monitoring;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bookmarkarr.Tests.Features.Infrastructure.Downloads.Monitoring
{
    /// <summary>
    /// A download whose state stops moving looks healthy on every individual poll — a status, a
    /// progress figure, no error — so nothing ever says it has gone nowhere for days. These cover
    /// the bookkeeping that turns "not moving" into something the log can report.
    /// </summary>
    [Trait("Name", "DownloadStallDetectionTests")]
    [Trait("Category", "DownloadMonitor")]
    public class DownloadStallDetectionTests
    {
        private static readonly DownloadClientConfiguration Client = new() { Id = "client-1", Name = "Test Client" };

        [Fact]
        [Trait("Scenario", "The first sighting of a state starts the clock rather than reporting a stall")]
        public void TrackProgressStall_OnFirstObservation_RecordsTheStateChange()
        {
            var (processor, _) = CreateProcessor();
            var download = BuildDownload(DownloadStatus.Downloading, progress: 10);

            processor.TrackProgressStall(Client, download, previous: null);

            Assert.Equal("Downloading:10", download.GetMetadataString(DownloadMonitorProcessor.StallFingerprintKey));
            Assert.NotNull(download.GetMetadataString(DownloadMonitorProcessor.StateChangedAtKey));
            Assert.Null(download.GetMetadataString(DownloadMonitorProcessor.StallWarnedAtKey));
        }

        [Fact]
        [Trait("Scenario", "A state that keeps changing never accumulates a stall")]
        public void TrackProgressStall_WhenProgressMoves_ResetsTheClock()
        {
            var (processor, time) = CreateProcessor();
            var download = BuildDownload(DownloadStatus.Downloading, progress: 10);
            processor.TrackProgressStall(Client, download, previous: null);
            var firstChangedAt = download.GetMetadataString(DownloadMonitorProcessor.StateChangedAtKey);

            time.Advance(DownloadMonitorProcessor.StallWarningThreshold * 2);
            download.Progress = 42;
            processor.TrackProgressStall(Client, download, BuildDownload(DownloadStatus.Downloading, progress: 10));

            Assert.Equal("Downloading:42", download.GetMetadataString(DownloadMonitorProcessor.StallFingerprintKey));
            Assert.NotEqual(firstChangedAt, download.GetMetadataString(DownloadMonitorProcessor.StateChangedAtKey));
            Assert.Null(download.GetMetadataString(DownloadMonitorProcessor.StallWarnedAtKey));
        }

        [Fact]
        [Trait("Scenario", "An unchanged state past the threshold is marked as warned exactly once")]
        public void TrackProgressStall_WhenStateIsUnchangedPastThreshold_WarnsOnceAndThenHolds()
        {
            var (processor, time) = CreateProcessor();
            var download = BuildDownload(DownloadStatus.Paused, progress: 0);
            var previous = BuildDownload(DownloadStatus.Paused, progress: 0);

            processor.TrackProgressStall(Client, download, previous: null);

            // Still inside the window: nothing to say yet.
            time.Advance(DownloadMonitorProcessor.StallWarningThreshold - TimeSpan.FromMinutes(1));
            processor.TrackProgressStall(Client, download, previous);
            Assert.Null(download.GetMetadataString(DownloadMonitorProcessor.StallWarnedAtKey));

            time.Advance(TimeSpan.FromMinutes(2));
            processor.TrackProgressStall(Client, download, previous);
            var warnedAt = download.GetMetadataString(DownloadMonitorProcessor.StallWarnedAtKey);
            Assert.NotNull(warnedAt);

            // The monitor polls every 30s; the warning must not repeat on each one.
            time.Advance(TimeSpan.FromMinutes(1));
            processor.TrackProgressStall(Client, download, previous);
            Assert.Equal(warnedAt, download.GetMetadataString(DownloadMonitorProcessor.StallWarnedAtKey));

            // A stall that outlives another full window is worth saying again.
            time.Advance(DownloadMonitorProcessor.StallWarningThreshold);
            processor.TrackProgressStall(Client, download, previous);
            Assert.NotEqual(warnedAt, download.GetMetadataString(DownloadMonitorProcessor.StallWarnedAtKey));
        }

        [Theory]
        [Trait("Scenario", "Import-owned and finished states are deliberately frozen, not stalled")]
        [InlineData(DownloadStatus.Completed)]
        [InlineData(DownloadStatus.Moved)]
        [InlineData(DownloadStatus.Failed)]
        [InlineData(DownloadStatus.Processing)]
        [InlineData(DownloadStatus.ImportPending)]
        [InlineData(DownloadStatus.ImportBlocked)]
        [InlineData(DownloadStatus.ImportNameMismatch)]
        [InlineData(DownloadStatus.SourceMissing)]
        public void TrackProgressStall_ForStatesOwnedByOtherWorkers_RecordsNothing(DownloadStatus status)
        {
            var (processor, time) = CreateProcessor();
            var download = BuildDownload(status, progress: 100);

            processor.TrackProgressStall(Client, download, previous: null);
            time.Advance(DownloadMonitorProcessor.StallWarningThreshold * 3);
            processor.TrackProgressStall(Client, download, BuildDownload(status, progress: 100));

            Assert.Null(download.GetMetadataString(DownloadMonitorProcessor.StallFingerprintKey));
            Assert.Null(download.GetMetadataString(DownloadMonitorProcessor.StallWarnedAtKey));
        }

        [Fact]
        [Trait("Scenario", "A torrent the client has called stalled for hours is given up on")]
        public void TrackProgressStall_WhenTheClientReportsAStallPastTheAbandonThreshold_FailsTheDownload()
        {
            var (processor, time) = CreateProcessor();
            var download = BuildDownload(DownloadStatus.Downloading, progress: 3, clientState: "stalledDL");
            var previous = BuildDownload(DownloadStatus.Downloading, progress: 3, clientState: "stalledDL");

            processor.TrackProgressStall(Client, download, previous: null);

            // Still inside the window: the download is left alone to recover on its own.
            time.Advance(DownloadStallState.AbandonThreshold - TimeSpan.FromMinutes(1));
            processor.TrackProgressStall(Client, download, previous);
            Assert.Equal(DownloadStatus.Downloading, download.Status);

            time.Advance(TimeSpan.FromMinutes(2));
            processor.TrackProgressStall(Client, download, previous);

            Assert.Equal(DownloadStatus.Failed, download.Status);
            Assert.Contains("Stalled in the download client", download.ErrorMessage);
            // Carried into the blocklist entry, so the reason survives past the download row.
            Assert.Contains("stalledDL", download.GetMetadataString("ClientFailureReason"));
        }

        [Fact]
        [Trait("Scenario", "A download frozen for hours that the client still calls healthy is reported, not abandoned")]
        public void TrackProgressStall_WhenTheClientReportsNoStall_WarnsWithoutFailing()
        {
            var (processor, time) = CreateProcessor();
            var download = BuildDownload(DownloadStatus.Downloading, progress: 3, clientState: "downloading");
            var previous = BuildDownload(DownloadStatus.Downloading, progress: 3, clientState: "downloading");

            processor.TrackProgressStall(Client, download, previous: null);
            time.Advance(DownloadStallState.AbandonThreshold + TimeSpan.FromHours(1));
            processor.TrackProgressStall(Client, download, previous);

            Assert.Equal(DownloadStatus.Downloading, download.Status);
            Assert.NotNull(download.GetMetadataString(DownloadMonitorProcessor.StallWarnedAtKey));
        }

        [Fact]
        [Trait("Scenario", "A seeding torrent with no leechers is not a failed download")]
        public void TrackProgressStall_ForAStalledUpload_DoesNotFailTheDownload()
        {
            var (processor, time) = CreateProcessor();
            var download = BuildDownload(DownloadStatus.Downloading, progress: 100, clientState: "stalledUP");
            var previous = BuildDownload(DownloadStatus.Downloading, progress: 100, clientState: "stalledUP");

            processor.TrackProgressStall(Client, download, previous: null);
            time.Advance(DownloadStallState.AbandonThreshold * 2);
            processor.TrackProgressStall(Client, download, previous);

            Assert.Equal(DownloadStatus.Downloading, download.Status);
        }

        [Fact]
        [Trait("Scenario", "A stall that keeps recovering never reaches the abandon threshold")]
        public void TrackProgressStall_WhenProgressResumes_NeverFailsTheDownload()
        {
            var (processor, time) = CreateProcessor();
            var download = BuildDownload(DownloadStatus.Downloading, progress: 3, clientState: "stalledDL");
            processor.TrackProgressStall(Client, download, previous: null);

            // qBittorrent reports stalledDL whenever the rate is momentarily zero, so a healthy
            // torrent flickers into it repeatedly. Each recovery has to reset the clock.
            for (var i = 0; i < 5; i++)
            {
                var previous = BuildDownload(DownloadStatus.Downloading, download.Progress, "stalledDL");
                time.Advance(DownloadStallState.AbandonThreshold - TimeSpan.FromMinutes(5));
                download.Progress += 1;
                processor.TrackProgressStall(Client, download, previous);
            }

            Assert.Equal(DownloadStatus.Downloading, download.Status);
        }

        private static Download BuildDownload(DownloadStatus status, decimal progress, string? clientState = null)
        {
            var download = new Download
            {
                Id = "download-1",
                Title = "A Book",
                Status = status,
                Progress = progress
            };

            if (clientState != null)
            {
                download.SetMetadata(DownloadStallState.ClientStateKey, clientState);
            }

            return download;
        }

        private static (DownloadMonitorProcessor Processor, MutableTimeProvider Time) CreateProcessor()
        {
            var time = new MutableTimeProvider(new DateTimeOffset(2026, 8, 16, 9, 0, 0, TimeSpan.Zero));
            var processor = new DownloadMonitorProcessor(
                new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
                Mock.Of<IDownloadPushService>(),
                time,
                NullLogger<DownloadMonitorProcessor>.Instance);

            return (processor, time);
        }

        private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
        {
            private DateTimeOffset _utcNow = utcNow;

            public override DateTimeOffset GetUtcNow() => _utcNow;

            public void Advance(TimeSpan duration) => _utcNow += duration;
        }
    }
}
