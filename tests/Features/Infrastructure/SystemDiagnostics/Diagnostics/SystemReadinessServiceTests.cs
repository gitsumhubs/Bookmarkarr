/*
 * Bookmarkarr - Audiobook Management System
 * Copyright (C) 2024-2026 Bookmarkarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bookmarkarr.Tests.Features.Infrastructure.SystemDiagnostics.Diagnostics;

public sealed class SystemReadinessServiceTests : IAsyncLifetime
{
    private readonly string _databasePath =
        Path.Join(Path.GetTempPath(), "bookmarkarr-tests", $"readiness-{Guid.NewGuid():N}.db");
    private IDbContextFactory<BookmarkarrDbContext> _factory = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        var options = new DbContextOptionsBuilder<BookmarkarrDbContext>()
            .UseSqlite($"Data Source={_databasePath};Pooling=False")
            .Options;
        _factory = new TestDbContextFactory(options);
        await using var db = await _factory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync()
    {
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task CheckAsync_MigratedDatabase_IsReady()
    {
        var service = new SystemReadinessService(
            _factory,
            NullLogger<SystemReadinessService>.Instance);

        var result = await service.CheckAsync();

        Assert.True(result.IsReady);
        Assert.True(result.DatabaseConnected);
        Assert.True(result.MigrationsCurrent);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public async Task CheckAsync_FactoryFailure_ReturnsNotReadyWithoutLeakingException()
    {
        var factory = new Mock<IDbContextFactory<BookmarkarrDbContext>>();
        factory.Setup(item => item.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("secret database path"));
        var service = new SystemReadinessService(
            factory.Object,
            NullLogger<SystemReadinessService>.Instance);

        var result = await service.CheckAsync();

        Assert.False(result.IsReady);
        Assert.Equal("database_check_failed", result.ErrorCode);
        Assert.False(result.DatabaseConnected);
    }

    private sealed class TestDbContextFactory(DbContextOptions<BookmarkarrDbContext> options)
        : IDbContextFactory<BookmarkarrDbContext>
    {
        public BookmarkarrDbContext CreateDbContext() => new(options);

        public Task<BookmarkarrDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
