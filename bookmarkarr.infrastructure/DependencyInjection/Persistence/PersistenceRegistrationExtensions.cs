/*
 * Bookmarkarr - Audiobook Management System
 * Copyright (C) 2024-2026 Bookmarkarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using Bookmarkarr.Infrastructure.Persistence;
using Bookmarkarr.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bookmarkarr.Infrastructure.DependencyInjection.Persistence;

internal static class PersistenceRegistrationExtensions
{
    public static IServiceCollection AddPersistenceServices(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder>? configureDb)
    {
        if (configureDb != null)
        {
            services.AddDbContextFactory<BookmarkarrDbContext>(
                configureDb,
                ServiceLifetime.Singleton);
        }

        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddSingleton<IMoveQueuePersistence, EfMoveQueuePersistence>();
        services.AddScoped<IHistoryRepository, EfHistoryRepository>();
        return services;
    }
}
