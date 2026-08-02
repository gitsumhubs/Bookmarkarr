/*
 * Bookmarkarr - Audiobook Management System
 * Copyright (C) 2024-2026 Bookmarkarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using Bookmarkarr.Infrastructure.DependencyInjection.DownloadClients;
using Bookmarkarr.Infrastructure.DependencyInjection.Downloads;
using Bookmarkarr.Infrastructure.DependencyInjection.Metadata;
using Bookmarkarr.Infrastructure.DependencyInjection.Platform;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bookmarkarr.Infrastructure.DependencyInjection;

/// <summary>
/// Compatibility composition surface for infrastructure HTTP clients and adapters.
/// Feature modules own the individual registrations.
/// </summary>
public static class ServiceRegistrationExtensions
{
    public static IServiceCollection AddBookmarkarrHttpClients(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddPlatformHttpClients(configuration);
        services.AddDownloadClientHttpClients();
        services.AddDownloadHttpClients();
        services.AddMetadataHttpClients(configuration);
        return services;
    }

    public static IServiceCollection AddBookmarkarrAdapters(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDownloadClientAdapters(configuration);
        services.AddPlatformAdapters();
        return services;
    }
}
