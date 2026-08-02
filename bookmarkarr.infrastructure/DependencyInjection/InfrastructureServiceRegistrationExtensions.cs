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
using Bookmarkarr.Infrastructure.DependencyInjection.Persistence;
using Bookmarkarr.Infrastructure.DependencyInjection.Search;
using Bookmarkarr.Infrastructure.DependencyInjection.Security;
using Bookmarkarr.Infrastructure.DependencyInjection.SystemDiagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bookmarkarr.Infrastructure.DependencyInjection;

/// <summary>
/// Compatibility composition surface for infrastructure implementations.
/// </summary>
public static class InfrastructureServiceRegistrationExtensions
{
    public static IServiceCollection AddBookmarkarrInfrastructure(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder>? configureDb = null,
        string? contentRootPath = null)
    {
        services.AddPersistenceServices(configureDb);
        services.AddDownloadInfrastructure();
        services.AddLibraryInfrastructure();
        services.AddSearchInfrastructure();
        services.AddMetadataInfrastructure();
        services.AddSecurityInfrastructure();
        services.AddSystemDiagnosticInfrastructure(contentRootPath);
        return services;
    }
}
