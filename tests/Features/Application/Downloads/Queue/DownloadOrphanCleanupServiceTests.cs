using Bookmarkarr.Tests.Builders;
using Bookmarkarr.Tests.Common;

namespace Bookmarkarr.Tests.Features.Application.Downloads.Queue
{
    [Trait("Name", "DownloadOrphanCleanupServiceTests")]
    [Trait("Category", "DownloadOrphanCleanupService")]
    public sealed class DownloadOrphanCleanupServiceTests : BaseTests
    {
        private Mock<IAppMetricsService> _metricsMock = null!;

        public override async Task InitializeAsync()
        {
            _metricsMock = new Mock<IAppMetricsService>();
            Init(services => services.WithSingleton<IAppMetricsService>(_metricsMock.Object));

            await base.InitializeAsync();
        }

        [Fact]
        [Trait("Method", "RemoveOrphansAsync")]
        public async Task RemoveOrphansAsync_RemovesOldActiveNonDdlDownloadWithoutExternalId()
        {
            var client = CreateClient();
            var download = await AddDownloadAsync(
                id: "missing-external-id",
                clientId: client.Id,
                status: DownloadStatus.Downloading,
                startedAt: DateTime.UtcNow.AddMinutes(-10));
            var service = _provider.GetRequiredService<DownloadOrphanCleanupService>();

            await service.RemoveOrphansAsync(
                client,
                CreateLiveSnapshot(client, [new QueueItem { Id = "other-live-item" }]),
                [new QueueItem { Id = "other-live-item" }],
                [download]);

            Assert.Equal(DownloadStatus.Downloading, (await _downloadRepository.GetByIdAsync(download.Id))!.Status);
            await ConfirmMissingAsync(service, client, download);
            Assert.Equal(DownloadStatus.SourceMissing, (await _downloadRepository.GetByIdAsync(download.Id))!.Status);
            _metricsMock.Verify(m => m.Increment("download.orphan.source_missing", 1), Times.Once);
        }

        [Fact]
        [Trait("Method", "RemoveOrphansAsync")]
        public async Task RemoveOrphansAsync_DoesNotRemoveDdlDownloadWithoutExternalId()
        {
            var client = CreateClient();
            var download = await AddDownloadAsync(
                id: "ddl-download",
                clientId: "DDL",
                status: DownloadStatus.Downloading,
                startedAt: DateTime.UtcNow.AddMinutes(-10));
            var service = _provider.GetRequiredService<DownloadOrphanCleanupService>();

            await service.RemoveOrphansAsync(
                client,
                CreateLiveSnapshot(client, [new QueueItem { Id = "other-live-item" }]),
                [new QueueItem { Id = "other-live-item" }],
                [download]);

            Assert.NotNull(await _downloadRepository.GetByIdAsync(download.Id));
            _metricsMock.Verify(m => m.Increment(It.IsAny<string>(), It.IsAny<double>()), Times.Never);
        }

        [Fact]
        [Trait("Method", "RemoveOrphansAsync")]
        public async Task RemoveOrphansAsync_DoesNotRemoveRecentNonDdlDownloadWithoutExternalId()
        {
            var client = CreateClient();
            var download = await AddDownloadAsync(
                id: "recent-download",
                clientId: client.Id,
                status: DownloadStatus.Downloading,
                startedAt: DateTime.UtcNow.AddMinutes(-1));
            var service = _provider.GetRequiredService<DownloadOrphanCleanupService>();

            await service.RemoveOrphansAsync(
                client,
                CreateLiveSnapshot(client, [new QueueItem { Id = "other-live-item" }]),
                [new QueueItem { Id = "other-live-item" }],
                [download]);

            Assert.NotNull(await _downloadRepository.GetByIdAsync(download.Id));
            _metricsMock.Verify(m => m.Increment(It.IsAny<string>(), It.IsAny<double>()), Times.Never);
        }

        [Fact]
        [Trait("Method", "RemoveOrphansAsync")]
        public async Task RemoveOrphansAsync_DoesNotRemoveIdlessDownloadWhenLiveSnapshotIsEmpty()
        {
            var client = CreateClient();
            var download = await AddDownloadAsync(
                id: "empty-snapshot-download",
                clientId: client.Id,
                status: DownloadStatus.Downloading,
                startedAt: DateTime.UtcNow.AddMinutes(-10));
            var service = _provider.GetRequiredService<DownloadOrphanCleanupService>();

            await service.RemoveOrphansAsync(
                client,
                CreateLiveSnapshot(client, []),
                [],
                [download]);

            Assert.NotNull(await _downloadRepository.GetByIdAsync(download.Id));
            _metricsMock.Verify(m => m.Increment(It.IsAny<string>(), It.IsAny<double>()), Times.Never);
        }

        [Theory]
        [Trait("Method", "RemoveOrphansAsync")]
        [InlineData(DownloadStatus.Completed)]
        [InlineData(DownloadStatus.Processing)]
        [InlineData(DownloadStatus.ImportPending)]
        [InlineData(DownloadStatus.Moved)]
        [InlineData(DownloadStatus.Failed)]
        public async Task RemoveOrphansAsync_DoesNotRemoveProtectedStatusesWithoutExternalId(DownloadStatus status)
        {
            var client = CreateClient();
            var download = await AddDownloadAsync(
                id: $"protected-{status}",
                clientId: client.Id,
                status: status,
                startedAt: DateTime.UtcNow.AddMinutes(-10));
            var service = _provider.GetRequiredService<DownloadOrphanCleanupService>();

            await service.RemoveOrphansAsync(
                client,
                CreateLiveSnapshot(client, [new QueueItem { Id = "other-live-item" }]),
                [new QueueItem { Id = "other-live-item" }],
                [download]);

            Assert.NotNull(await _downloadRepository.GetByIdAsync(download.Id));
            _metricsMock.Verify(m => m.Increment(It.IsAny<string>(), It.IsAny<double>()), Times.Never);
        }

        [Fact]
        [Trait("Method", "RemoveOrphansAsync")]
        public async Task RemoveOrphansAsync_RemovesKnownExternalIdMissingFromLiveSnapshot()
        {
            var client = CreateClient();
            var download = await AddDownloadAsync(
                id: "known-orphan",
                clientId: client.Id,
                status: DownloadStatus.Downloading,
                startedAt: DateTime.UtcNow.AddMinutes(-10),
                metadata: new Dictionary<string, object>
                {
                    ["ClientDownloadId"] = "missing-client-id"
                });
            var service = _provider.GetRequiredService<DownloadOrphanCleanupService>();

            await service.RemoveOrphansAsync(
                client,
                CreateLiveSnapshot(client, [new QueueItem { Id = "other-live-item" }]),
                [new QueueItem { Id = "other-live-item" }],
                [download]);

            await ConfirmMissingAsync(service, client, download);
            var stored = await _downloadRepository.GetByIdAsync(download.Id);
            Assert.Equal(DownloadStatus.SourceMissing, stored!.Status);
            Assert.Equal("missing-client-id", stored.GetMetadataString("ClientDownloadId"));
            _metricsMock.Verify(m => m.Increment("download.orphan.source_missing", 1), Times.Once);
        }

        [Theory]
        [Trait("Method", "RemoveOrphansAsync")]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public async Task RemoveOrphansAsync_DoesNotRemoveWhenSnapshotIsNotTrusted(bool usedCachedSnapshot, bool isUnavailable)
        {
            var client = CreateClient();
            var download = await AddDownloadAsync(
                id: "untrusted-snapshot-download",
                clientId: client.Id,
                status: DownloadStatus.Downloading,
                startedAt: DateTime.UtcNow.AddMinutes(-10));
            var service = _provider.GetRequiredService<DownloadOrphanCleanupService>();

            await service.RemoveOrphansAsync(
                client,
                new ClientQueueFetchResult(
                    client,
                    [new QueueItem { Id = "other-live-item" }],
                    usedCachedSnapshot,
                    isUnavailable,
                    snapshotAge: null,
                    failureReason: isUnavailable ? "unavailable" : null,
                    snapshotState: isUnavailable ? "unavailable" : "cached",
                    snapshotRefreshedAtUtc: DateTimeOffset.UtcNow),
                [new QueueItem { Id = "other-live-item" }],
                [download]);

            Assert.NotNull(await _downloadRepository.GetByIdAsync(download.Id));
            _metricsMock.Verify(m => m.Increment(It.IsAny<string>(), It.IsAny<double>()), Times.Never);
        }

        private async Task<Download> AddDownloadAsync(
            string id,
            string clientId,
            DownloadStatus status,
            DateTime startedAt,
            Dictionary<string, object>? metadata = null)
        {
            var download = new DownloadBuilder()
                .WithId(id)
                .WithStatus(status)
                .WithStartDate(startedAt)
                .WithTitle(id)
                .Build();

            download.DownloadClientId = clientId;
            download.Metadata = metadata ?? new Dictionary<string, object>();

            return await _downloadRepository.AddAsync(download);
        }

        private static Task ConfirmMissingAsync(DownloadOrphanCleanupService service, DownloadClientConfiguration client, Download download)
        {
            var later = DateTimeOffset.Parse(download.GetMetadataString("QueueMissingSince")!).AddMinutes(6);
            return service.RemoveOrphansAsync(client, new ClientQueueFetchResult(client, [], false, false, null, null, "live", later), [], [download]);
        }

        [Fact]
        public async Task RepeatedLiveEmptySnapshots_MarkMissingButRetainHistory()
        {
            var client = CreateClient();
            var download = await AddDownloadAsync("empty-confirmed", client.Id, DownloadStatus.Downloading, DateTime.UtcNow.AddHours(-2));
            var service = _provider.GetRequiredService<DownloadOrphanCleanupService>();
            var snapshot = CreateLiveSnapshot(client, []);
            await service.RemoveOrphansAsync(client, snapshot, [], [download]);
            await service.RemoveOrphansAsync(client, snapshot, [], [download]);
            Assert.Equal(DownloadStatus.Downloading, (await _downloadRepository.GetByIdAsync(download.Id))!.Status);
            await ConfirmMissingAsync(service, client, download);
            Assert.Equal(DownloadStatus.SourceMissing, (await _downloadRepository.GetByIdAsync(download.Id))!.Status);
        }

        [Fact]
        public async Task ReturningItem_ClearsAbsenceEvidence()
        {
            var client = CreateClient();
            var download = await AddDownloadAsync("returned", client.Id, DownloadStatus.Downloading, DateTime.UtcNow.AddHours(-2),
                new Dictionary<string, object> { ["ClientDownloadId"] = "external" });
            var service = _provider.GetRequiredService<DownloadOrphanCleanupService>();
            await service.RemoveOrphansAsync(client, CreateLiveSnapshot(client, []), [], [download]);
            await service.RemoveOrphansAsync(client, CreateLiveSnapshot(client, [new QueueItem { Id = "external" }]), [], [download]);
            Assert.Null((await _downloadRepository.GetByIdAsync(download.Id))!.GetMetadataString("QueueMissingSince"));
        }

        private static DownloadClientConfiguration CreateClient() => new()
        {
            Id = "client-1",
            Name = "Mock Client",
            Type = "mock",
            IsEnabled = true
        };

        private static ClientQueueFetchResult CreateLiveSnapshot(
            DownloadClientConfiguration client,
            List<QueueItem> queueItems) => new(
                client,
                queueItems,
                usedCachedSnapshot: false,
                isUnavailable: false,
                snapshotAge: TimeSpan.Zero,
                failureReason: null,
                snapshotState: "live",
                snapshotRefreshedAtUtc: DateTimeOffset.UtcNow);
    }
}
