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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bookmarkarr.Infrastructure.SystemDiagnostics.Diagnostics;

public sealed class SystemReadinessService(
    IDbContextFactory<BookmarkarrDbContext> dbFactory,
    ILogger<SystemReadinessService> logger) : ISystemReadinessService
{
    public async Task<SystemReadiness> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            if (!await db.Database.CanConnectAsync(cancellationToken))
            {
                return new SystemReadiness(false, "not_ready", false, false, "database_unavailable");
            }

            var pendingMigrations = await db.Database.GetPendingMigrationsAsync(cancellationToken);
            var migrationsCurrent = !pendingMigrations.Any();
            return migrationsCurrent
                ? new SystemReadiness(true, "ready", true, true)
                : new SystemReadiness(false, "not_ready", true, false, "pending_migrations");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is not (OperationCanceledException or OutOfMemoryException or StackOverflowException))
        {
            logger.LogError(exception, "Readiness database check failed");
            return new SystemReadiness(false, "not_ready", false, false, "database_check_failed");
        }
    }
}
