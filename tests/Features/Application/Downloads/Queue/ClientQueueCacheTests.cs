/* Bookmarkarr is licensed under the GNU AGPL v3 or later. */
using Microsoft.Extensions.Caching.Memory;
namespace Bookmarkarr.Tests.Features.Application.Downloads.Queue;
public sealed class ClientQueueCacheTests
{
    [Fact]
    public async Task ConcurrentReadsShareOneFetch_ConsumersReceiveIndependentCopies()
    {
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var cache = new ClientQueueRequestCache(memory);
        var client = new DownloadClientConfiguration { Id = "one" };
        var gate = new TaskCompletionSource<List<QueueItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        Task<List<QueueItem>> Fetch() { Interlocked.Increment(ref count); return gate.Task; }
        var first = cache.GetAsync(client, null, Fetch, default);
        var second = cache.GetAsync(client, null, Fetch, default);
        gate.SetResult([new QueueItem { Id = "original" }]);
        var results = await Task.WhenAll(first, second);
        Assert.Equal(1, count);
        results[0][0].Id = "mutated";
        Assert.Equal("original", results[1][0].Id);
        cache.Invalidate();
        await cache.GetAsync(client, null, Fetch, default);
        Assert.Equal(2, count);
    }
    [Fact]
    public async Task PollerSharesRecentSuccessfulSnapshotWithoutCallingClientAgain()
    {
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var fetcher = new Mock<IDownloadClientQueueFetcher>();
        fetcher.Setup(f => f.GetQueueAsync(It.IsAny<DownloadClientConfiguration>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<QueueItem>());
        var poller = new DownloadClientQueuePoller(memory, fetcher.Object, Mock.Of<IAppMetricsService>(), Microsoft.Extensions.Logging.Abstractions.NullLogger<DownloadClientQueuePoller>.Instance);
        var client = new DownloadClientConfiguration { Id = "one" };
        var results = await Task.WhenAll(poller.FetchAsync([client], TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1), 1), poller.FetchAsync([client], TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1), 1));
        fetcher.Verify(f => f.GetQueueAsync(client, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(results[0][0].SnapshotRefreshedAtUtc, results[1][0].SnapshotRefreshedAtUtc);
        Assert.All(results, r => Assert.False(r[0].IsUnavailable));
    }
}
