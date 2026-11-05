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

using Bookmarkarr.Application.Audiobooks.Matching;
using Bookmarkarr.Application.Search.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Bookmarkarr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Bookmarkarr.Infrastructure.HostedServices.Search
{
    public class AutomaticSearchService(
        ILogger<AutomaticSearchService> logger,
        IAutomaticSearchProcessor processor,
        IWorkerCycleRunner cycleRunner) : BackgroundService
    {
        private static readonly TimeSpan SearchInterval = TimeSpan.FromHours(6);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("AutomaticSearchService started. Will search monitored editions every {Hours} hours", SearchInterval.TotalHours);

            await cycleRunner.RunPeriodicAsync(
                nameof(AutomaticSearchService),
                initialDelay: TimeSpan.FromMinutes(5),
                intervalProvider: () => SearchInterval,
                runCycle: processor.RunCycleAsync,
                stoppingToken);

            logger.LogInformation("AutomaticSearchService stopped");
        }
    }

    public class AutomaticSearchProcessor(
        ILogger<AutomaticSearchProcessor> logger,
        IServiceScopeFactory scopeFactory) : IAutomaticSearchProcessor
    {
        public async Task RunCycleAsync(CancellationToken cancellationToken)
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BookmarkarrDbContext>();
            var configuration = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
            var settings = await configuration.GetApplicationSettingsAsync();
            if (settings.UnmonitorImportedEditions)
                await scope.ServiceProvider.GetRequiredService<IAudiobookRepository>().UnmonitorSatisfiedEditionsAsync(cancellationToken);
            var cutoff = DateTime.UtcNow.AddHours(-6);
            var ids = await db.BookEditions.AsNoTracking()
                .Where(e => e.Monitored && e.QualityProfileId != null
                    && (e.LastSearchTime == null || e.LastSearchTime < cutoff))
                .OrderBy(e => e.LastSearchTime).ThenBy(e => e.Id).Select(e => e.Id).ToListAsync(cancellationToken);
            var search = scope.ServiceProvider.GetRequiredService<IEditionSearchService>();
            logger.LogInformation("Automatic search: {Count} editions eligible", ids.Count);
            foreach (var id in ids)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { await search.SearchAndDownloadAsync(id, cancellationToken); }
                catch (IndexerUnavailableException ex) when (ex.IsRateLimited)
                {
                    logger.LogInformation("Automatic search paused for indexer quota: {Reason}", ex.Message);
                    break;
                }
                catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException or StackOverflowException))
                {
                    logger.LogError(ex, "Automatic search failed for edition {EditionId}", id);
                }
            }
        }
    }
}
