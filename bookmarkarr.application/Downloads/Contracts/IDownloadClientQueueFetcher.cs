/*
 * Bookmarkarr - Audiobook Management System
 * Copyright (C) 2024-2026 Bookmarkarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

namespace Bookmarkarr.Application.Downloads.Contracts;

/// <summary>
/// Fetches a client queue through an independently scoped gateway.
/// </summary>
public interface IDownloadClientQueueFetcher
{
    Task<List<QueueItem>> GetQueueAsync(
        DownloadClientConfiguration client,
        CancellationToken cancellationToken = default);
}
