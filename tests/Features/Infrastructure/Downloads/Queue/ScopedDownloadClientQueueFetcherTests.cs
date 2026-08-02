/*
 * Bookmarkarr - Audiobook Management System
 * Copyright (C) 2024-2026 Bookmarkarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
namespace Bookmarkarr.Tests.Features.Infrastructure.Downloads.Queue;

public sealed class ScopedDownloadClientQueueFetcherTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly TaskCompletionSource _bothPollsStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _createdGateways;
    private int _startedPolls;

    public ScopedDownloadClientQueueFetcherTests()
    {
        _provider = new ServiceCollection()
            .AddScoped<IDownloadClientGateway>(_ =>
            {
                Interlocked.Increment(ref _createdGateways);
                var gateway = new Mock<IDownloadClientGateway>(MockBehavior.Strict);
                gateway
                    .Setup(value => value.GetQueueAsync(
                        It.IsAny<DownloadClientConfiguration>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(async () =>
                    {
                        if (Interlocked.Increment(ref _startedPolls) == 2)
                        {
                            _bothPollsStarted.TrySetResult();
                        }

                        await _bothPollsStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                        return [];
                    });
                return gateway.Object;
            })
            .BuildServiceProvider(validateScopes: true);
    }

    [Fact]
    public async Task GetQueueAsync_CreatesIndependentScopeForEachConcurrentClientPoll()
    {
        var fetcher = new ScopedDownloadClientQueueFetcher(
            _provider.GetRequiredService<IServiceScopeFactory>());

        var results = await Task.WhenAll(
            fetcher.GetQueueAsync(new DownloadClientConfiguration { Id = "audio", Name = "Audio", IsEnabled = true }),
            fetcher.GetQueueAsync(new DownloadClientConfiguration { Id = "ebooks", Name = "Ebooks", IsEnabled = true }));

        Assert.Equal(2, results.Length);
        Assert.Equal(2, _createdGateways);
        Assert.Equal(2, _startedPolls);
    }

    public void Dispose() => _provider.Dispose();
}
