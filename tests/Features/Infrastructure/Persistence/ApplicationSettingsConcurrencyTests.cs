/*
 * Bookmarkarr - Audiobook Management System
 * Copyright (C) 2024-2026 Bookmarkarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Bookmarkarr.Infrastructure.Persistence.Repositories;
using Bookmarkarr.Application.Common.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Bookmarkarr.Tests.Features.Infrastructure.Persistence;

public sealed class ApplicationSettingsConcurrencyTests : IAsyncLifetime
{
    private readonly string _databasePath =
        Path.Join(Path.GetTempPath(), "bookmarkarr-tests", $"settings-concurrency-{Guid.NewGuid():N}.db");
    private DbContextOptions<BookmarkarrDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        _options = new DbContextOptionsBuilder<BookmarkarrDbContext>()
            .UseSqlite($"Data Source={_databasePath};Pooling=False")
            .Options;
        await using var db = new BookmarkarrDbContext(_options);
        await db.Database.EnsureCreatedAsync();
        await new EfApplicationSettingsRepository(db).SaveAsync(new ApplicationSettings());
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
    public async Task StaleSettingsUpdate_ThrowsStableConflict()
    {
        await using var db1 = new BookmarkarrDbContext(_options);
        await using var db2 = new BookmarkarrDbContext(_options);
        var repository1 = new EfApplicationSettingsRepository(db1);
        var repository2 = new EfApplicationSettingsRepository(db2);
        var first = await repository1.GetAsync();
        var stale = await repository2.GetAsync();
        Assert.NotNull(first);
        Assert.NotNull(stale);

        first!.OutputPath = "first";
        await repository1.SaveAsync(first);
        stale!.OutputPath = "stale";

        var exception = await Assert.ThrowsAsync<ApplicationConflictException>(
            () => repository2.SaveAsync(stale));

        Assert.Equal("settings_concurrency_conflict", exception.Code);
    }

    [Fact]
    public async Task ConcurrentFreshStart_CreatesOneCanonicalSettingsRowWithoutErrors()
    {
        await using (var cleanupDb = new BookmarkarrDbContext(_options))
        {
            await cleanupDb.ApplicationSettings.ExecuteDeleteAsync();
        }

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 8).Select(async candidate =>
        {
            await start.Task;
            await using var db = new BookmarkarrDbContext(_options);
            var repository = new EfApplicationSettingsRepository(db);
            return await repository.SaveAsync(new ApplicationSettings
            {
                OutputPath = $"candidate-{candidate}"
            });
        }).ToList();

        start.SetResult();
        var returnedSettings = await Task.WhenAll(tasks);

        Assert.All(returnedSettings, settings => Assert.Equal(1, settings.Id));
        await using var verificationDb = new BookmarkarrDbContext(_options);
        Assert.Equal(1, await verificationDb.ApplicationSettings.CountAsync());
        Assert.Equal(tasks.Count, (await verificationDb.ApplicationSettings.SingleAsync()).Version);
    }
}
