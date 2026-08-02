/*
 * Bookmarkarr - Audiobook Management System
 * Copyright (C) 2024-2026 Bookmarkarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using Bookmarkarr.Infrastructure.DependencyInjection.Downloads;
using Bookmarkarr.Infrastructure.DependencyInjection.Library;
using Bookmarkarr.Infrastructure.DependencyInjection.Metadata;
using Bookmarkarr.Infrastructure.DependencyInjection.Notifications;
using Bookmarkarr.Infrastructure.DependencyInjection.Search;
using Bookmarkarr.Infrastructure.DependencyInjection.Security;
using Bookmarkarr.Infrastructure.DependencyInjection.SystemDiagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bookmarkarr.Infrastructure.DependencyInjection;

/// <summary>
/// Compatibility composition surface for application-facing feature services.
/// </summary>
public static class AppServiceRegistrationExtensions
{
    public static IServiceCollection AddBookmarkarrAppServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddConfigurationAndSecurityServices();
        services.AddSearchServices();
        services.AddMetadataServices();
        services.AddLibraryServices();
        services.AddDownloadServices(configuration);
        services.AddNotificationAndRealtimeServices();
        services.AddSystemDiagnosticServices();
        return services;
    }
}
