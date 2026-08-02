/*
 * Bookmarkarr - Audiobook Management System
 * Copyright (C) 2024-2026 Bookmarkarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */

using Bookmarkarr.Infrastructure.Persistence;
using Bookmarkarr.Infrastructure.Persistence.Repositories;
using Bookmarkarr.Infrastructure.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Serilog;

namespace Bookmarkarr.Infrastructure.DependencyInjection;

public static class InfrastructureStartupCompositionExtensions
{
    public static IServiceCollection AddBookmarkarrInfrastructureComposition(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        services.AddBookmarkarrHttpClients(configuration);

        var sqliteDbPath = ResolveSqliteDbPath(configuration, environment);
        Log.Logger.Information("[Startup] Resolved SQLite DB path: {SqliteDbPath}", sqliteDbPath);

        services.AddBookmarkarrAdapters(configuration);
        services.AddBookmarkarrInfrastructure(options =>
            options.UseSqlite($"Data Source={sqliteDbPath}", sqliteOptions =>
                sqliteOptions.MigrationsAssembly(typeof(QualityProfileRepository).Assembly.GetName().Name)),
            environment.ContentRootPath);
        services.AddBookmarkarrAppServices(configuration);
        services.AddBookmarkarrHostedWorkers(configuration);
        services.AddBookmarkarrExternalRequests(configuration);
        services.AddScoped<IDownloadHistoryService, DownloadHistoryService>();

        return services;
    }

    public static void ApplyBookmarkarrDatabaseMigrations(this IServiceProvider serviceProvider)
    {
        try
        {
            Log.Logger.Information("[Startup] Applying EF Core migrations at startup");
            using var migrateScope = serviceProvider.CreateScope();
            var factory = migrateScope.ServiceProvider.GetRequiredService<IDbContextFactory<BookmarkarrDbContext>>();
            using var ctx = factory.CreateDbContext();
            ctx.Database.Migrate();
            Log.Logger.Information("[Startup] EF Core migrations applied successfully");
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
        {
            Log.Logger.Error(ex, "[Startup] Failed to apply EF Core migrations at startup. You can run 'dotnet ef database update' manually to apply migrations.");
        }
    }

    private static IServiceCollection AddBookmarkarrHostedWorkers(this IServiceCollection services, IConfiguration configuration)
    {
        var disableHostedServices =
            configuration.GetValue<bool>("Bookmarkarr:DisableHostedServices") ||
            string.Equals(Environment.GetEnvironmentVariable("BOOKMARKARR_DISABLE_HOSTED_SERVICES"), "true", StringComparison.OrdinalIgnoreCase);

        if (disableHostedServices)
        {
            Log.Logger.Warning("[Startup] Hosted/background services are disabled by configuration override");
        }
        else
        {
            Log.Logger.Information("[Startup] Hosted/background services are enabled");
        }

        services.AddSingleton<IUnmatchedScanQueueService, UnmatchedScanQueueService>();
        if (!disableHostedServices)
        {
            services.AddBookmarkarrHostedServices(configuration);
        }

        services.AddHostedService<StartupDbNormalizer>();
        return services;
    }

    private static IServiceCollection AddBookmarkarrExternalRequests(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ExternalRequestOptions>()
            .Bind(configuration.GetSection("ExternalRequests"))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ExternalRequestOptions>, ExternalRequestOptionsValidator>();

        return services;
    }

    private static string ResolveSqliteDbPath(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var sqliteDbPathOverride = configuration["Bookmarkarr:SqliteDbPath"];
        var sqliteDbPath = string.IsNullOrWhiteSpace(sqliteDbPathOverride)
            ? Path.Join(environment.ContentRootPath, "config", "database", "bookmarkarr.db")
            : (Path.IsPathRooted(sqliteDbPathOverride)
                ? sqliteDbPathOverride
                : Path.Join(environment.ContentRootPath, sqliteDbPathOverride));

        if (environment.IsEnvironment("Test"))
        {
            var repoDbPath = Path.GetFullPath(Path.Join(environment.ContentRootPath, "config", "database", "bookmarkarr.db"));
            var resolvedSqlitePath = Path.GetFullPath(sqliteDbPath);
            if (string.Equals(resolvedSqlitePath, repoDbPath, StringComparison.OrdinalIgnoreCase))
            {
                sqliteDbPath = Path.Join(Path.GetTempPath(), "bookmarkarr-tests", "program-main", $"bookmarkarr-{Guid.NewGuid():N}.db");
                Log.Logger.Warning("[Startup] Test environment attempted to use repo sqlite path; forcing isolated test DB path: {SqliteDbPath}", sqliteDbPath);
            }
        }

        var sqliteDbDir = Path.GetDirectoryName(sqliteDbPath);
        if (!string.IsNullOrEmpty(sqliteDbDir) && !Directory.Exists(sqliteDbDir))
        {
            Directory.CreateDirectory(sqliteDbDir);
        }

        return sqliteDbPath;
    }
}
