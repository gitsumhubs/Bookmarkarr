/* Bookmarkarr is licensed under the GNU AGPL v3 or later. */
using Bookmarkarr.Infrastructure.Realtime.Downloads;
using Bookmarkarr.Infrastructure.Realtime.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
namespace Bookmarkarr.Tests.Features.Infrastructure.Realtime;

public sealed class DownloadPushServiceTests
{
    [Fact]
    public async Task FirstPushAndChangedProgressArrive_IdenticalPushIsSuppressed()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var proxy = new Mock<IClientProxy>();
        var clients = new Mock<IHubClients>(); clients.SetupGet(c => c.All).Returns(proxy.Object);
        var hub = new Mock<IHubContext<DownloadHub>>(); hub.SetupGet(h => h.Clients).Returns(clients.Object);
        var service = new DownloadPushService(hub.Object, cache, NullLogger<DownloadPushService>.Instance);
        var download = new Download { Id = "first", EditionId = 9, Status = DownloadStatus.Downloading };
        await service.HandlePushAsync(download);
        download.SetMetadata("ClientLastSeenAt", DateTimeOffset.UtcNow.ToString("O"));
        await service.HandlePushAsync(download);
        download.Progress = 50;
        await service.HandlePushAsync(download);
        proxy.Verify(p => p.SendCoreAsync("DownloadUpdate", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task FullSnapshotsIncludeEveryItem_AndCanClearTheList()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var payloads = new List<object?[]>();
        var proxy = new Mock<IClientProxy>();
        proxy.Setup(p => p.SendCoreAsync("DownloadsList", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object?[], CancellationToken>((_, args, _) => payloads.Add(args)).Returns(Task.CompletedTask);
        var clients = new Mock<IHubClients>(); clients.SetupGet(c => c.All).Returns(proxy.Object);
        var hub = new Mock<IHubContext<DownloadHub>>(); hub.SetupGet(h => h.Clients).Returns(clients.Object);
        var service = new DownloadPushService(hub.Object, cache, NullLogger<DownloadPushService>.Instance);
        await service.HandlePushAsync(new List<Download> { new() { Id = "one" }, new() { Id = "two" } });
        await service.HandlePushAsync(new List<Download>());
        Assert.Equal(2, payloads.Count);
        Assert.Equal(2, Assert.IsAssignableFrom<System.Collections.ICollection>(payloads[0][0]).Count);
        Assert.Equal(0, Assert.IsAssignableFrom<System.Collections.ICollection>(payloads[1][0]).Count);
    }
}
