/*
 * Bookmarkarr - Audiobook Management System
 * Copyright (C) 2024-2026 Bookmarkarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using Microsoft.Extensions.DependencyInjection;

namespace Bookmarkarr.Infrastructure.Downloads.Queue;

public sealed class ScopedDownloadClientQueueFetcher(IServiceScopeFactory scopeFactory)
    : IDownloadClientQueueFetcher
{
    public async Task<List<QueueItem>> GetQueueAsync(
        DownloadClientConfiguration client,
        CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var gateway = scope.ServiceProvider.GetRequiredService<IDownloadClientGateway>();
        return await gateway.GetQueueAsync(client, cancellationToken);
    }
}
