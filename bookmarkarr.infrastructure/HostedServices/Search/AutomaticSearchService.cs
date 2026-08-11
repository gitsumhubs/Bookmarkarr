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
            logger.LogInformation("AutomaticSearchService started. Will search monitored audiobooks every {Hours} hours", SearchInterval.TotalHours);

            await cycleRunner.RunPeriodicAsync(
                nameof(AutomaticSearchService),
                initialDelay: TimeSpan.FromMinutes(5),
                intervalProvider: () => SearchInterval,
                runCycle: processor.RunCycleAsync,
                stoppingToken);

            logger.LogInformation("AutomaticSearchService stopped");
        }
    }

    public class AutomaticSearchProcessor : IAutomaticSearchProcessor
    {
        private readonly ILogger<AutomaticSearchProcessor> _logger;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly AutomaticSearchResultClassifier _resultClassifier;
        private readonly AutomaticSearchQualityEvaluator _qualityEvaluator;
        private readonly AutomaticSearchDownloadClientSelector _downloadClientSelector;

        public AutomaticSearchProcessor(
            ILogger<AutomaticSearchProcessor> logger,
            IServiceScopeFactory serviceScopeFactory)
        {
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;
            _resultClassifier = new AutomaticSearchResultClassifier(_logger);
            _qualityEvaluator = new AutomaticSearchQualityEvaluator(_logger);
            _downloadClientSelector = new AutomaticSearchDownloadClientSelector(_serviceScopeFactory, _logger);
        }

        public Task RunCycleAsync(CancellationToken cancellationToken) => PerformAutomaticSearchesAsync(cancellationToken);

        private async Task PerformAutomaticSearchesAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Starting automatic search cycle for monitored audiobooks");

            using var scope = _serviceScopeFactory.CreateScope();
            var audiobookRepository = scope.ServiceProvider.GetRequiredService<IAudiobookRepository>();
            var downloadRepository = scope.ServiceProvider.GetRequiredService<IDownloadRepository>();
            var fileRepository = scope.ServiceProvider.GetRequiredService<IAudiobookFileRepository>();
            var searchService = scope.ServiceProvider.GetRequiredService<ISearchService>();
            var qualityProfileService = scope.ServiceProvider.GetRequiredService<IQualityProfileService>();
            var downloadService = scope.ServiceProvider.GetRequiredService<IDownloadService>();
            var configurationService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
            var fileSystem = scope.ServiceProvider.GetRequiredService<IFileSystem>();
            var fileNamingService = scope.ServiceProvider.GetRequiredService<IFileNamingService>();
            var blocklistService = scope.ServiceProvider.GetRequiredService<IBlocklistService>();

            var searchThrottle = scope.ServiceProvider.GetRequiredService<ISearchThrottle>();
            var indexerRepository = scope.ServiceProvider.GetRequiredService<IIndexerRepository>();

            var settings = await configurationService.GetApplicationSettingsAsync();
            var presenceInspector = new LibraryPresenceInspector(fileSystem, fileNamingService, _logger);

            // Whether this pass is paced at all. Asked once per cycle rather than once per book:
            // the answer cannot change mid-pass in any way worth reacting to, and a usenet-only
            // install keeps searching at full speed.
            var enabledIndexers = await indexerRepository.GetEnabledAsync(true, stoppingToken);
            var torrentIndexerActive = IndexerProtocol.AnyTorrent(enabledIndexers);
            if (torrentIndexerActive)
            {
                _logger.LogInformation(
                    "A torrent indexer is enabled, so this pass searches one book at a time with at least {Seconds:F0}s between books",
                    searchThrottle.MinimumCooldown.TotalSeconds);
            }

            // Retire anything already satisfied before choosing what to search. Doing it here rather
            // than only at import means a library that was already finished stops searching too,
            // instead of waiting for an import that will never come.
            if (settings?.UnmonitorImportedEditions == true)
            {
                var retired = await audiobookRepository.UnmonitorSatisfiedEditionsAsync(stoppingToken);
                if (retired > 0)
                {
                    _logger.LogInformation(
                        "Stopped monitoring {Count} imported edition(s) that already hold files, so they are no longer searched",
                        retired);
                }
            }

            // Get all monitored audiobooks that haven't been searched in the last 6 hours
            var cutoffTime = DateTime.UtcNow.AddHours(-6);
            var monitoredAudiobooks = await audiobookRepository.GetMonitoredAudiobooksForSearchAsync(cutoffTime, stoppingToken);

            _logger.LogInformation("Found {Count} monitored audiobooks eligible for automatic search", monitoredAudiobooks.Count);

            if (!monitoredAudiobooks.Any())
            {
                _logger.LogInformation("No audiobooks require automatic search at this time");
                return;
            }

            var processedCount = 0;
            var downloadsQueued = 0;

            foreach (var audiobook in monitoredAudiobooks)
            {
                if (stoppingToken.IsCancellationRequested)
                    break;

                try
                {
                    // Held across the whole book — every query rung, every indexer, and the grab —
                    // so nothing else searches alongside it, and released with the cooldown armed
                    // so the next book starts a minute later however this one ended.
                    using var searchLease = await searchThrottle.AcquireBookAsync(
                        audiobook.Title, torrentIndexerActive, stoppingToken);

                    var downloadsQueuedForBook = await ProcessAudiobookAsync(
                        audiobook, searchService, qualityProfileService, downloadService, audiobookRepository, downloadRepository, fileRepository,
                        presenceInspector, settings, blocklistService, stoppingToken);

                    downloadsQueued += downloadsQueuedForBook;
                    processedCount++;

                    // Update last search time
                    audiobook.LastSearchTime = DateTime.UtcNow;
                    await audiobookRepository.UpdateAsync(audiobook);

                    _logger.LogInformation("Processed audiobook '{Title}' - queued {QueuedCount} downloads",
                        audiobook.Title, downloadsQueuedForBook);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                // A spent budget is not this book's failure. Stamping LastSearchTime here would put
                // it on a six-hour cooldown for a search that never happened, so the pass ends
                // instead and the book stays eligible. Ordering by LastSearchTime means the next
                // pass resumes here rather than starting over at the head of the list.
                catch (IndexerUnavailableException ex) when (ex.IsRateLimited)
                {
                    _logger.LogInformation(
                        "Stopping this automatic search pass: {Indexer} is rate limited ({Reason}). {Remaining} book(s) stay eligible for the next pass.",
                        ex.IndexerName,
                        ex.Message,
                        monitoredAudiobooks.Count - processedCount);
                    break;
                }
                catch (OperationCanceledException ex)
                {
                    _logger.LogWarning(ex, "Automatic search processing canceled/timed out for audiobook '{Title}' (ID: {Id})", audiobook.Title, audiobook.Id);
                }
                catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
                {
                    _logger.LogError(ex, "Error processing audiobook '{Title}' (ID: {Id})", audiobook.Title, audiobook.Id);
                }
            }

            _logger.LogInformation("Automatic search cycle completed. Processed {ProcessedCount} audiobooks, queued {DownloadsQueued} total downloads",
                processedCount, downloadsQueued);
        }

        private async Task<int> ProcessAudiobookAsync(
            Audiobook audiobook,
            ISearchService searchService,
            IQualityProfileService qualityProfileService,
            IDownloadService downloadService,
            IAudiobookRepository audiobookRepository,
            IDownloadRepository downloadRepository,
            IAudiobookFileRepository fileRepository,
            LibraryPresenceInspector presenceInspector,
            ApplicationSettings settings,
            IBlocklistService blocklistService,
            CancellationToken stoppingToken)
        {
            if (audiobook.QualityProfile == null)
            {
                _logger.LogWarning("Audiobook '{Title}' has no quality profile assigned", audiobook.Title);
                return 0;
            }

            // Check if there's already an active download for this audiobook
            var allDownloads = await downloadRepository.GetByAudiobookIdAsync(audiobook.Id, stoppingToken);
            var activeDownload = allDownloads.FirstOrDefault(d =>
                d.Status == DownloadStatus.Queued ||
                d.Status == DownloadStatus.Downloading ||
                d.Status == DownloadStatus.Paused ||
                d.Status == DownloadStatus.Processing ||
                d.Status == DownloadStatus.Ready ||
                d.Status == DownloadStatus.ImportPending);

            if (activeDownload != null)
            {
                _logger.LogInformation("Audiobook '{Title}' already has an active download (ID: {DownloadId}, Status: {Status}), skipping automatic search",
                    audiobook.Title, activeDownload.Id, activeDownload.Status);
                return 0;
            }

            // Ask the filesystem, not just the database. A book whose files were imported before it
            // was tracked, or whose import failed after the bytes landed, has no file rows at all —
            // and a row-only check reads that as "missing" and grabs it again.
            var diskPresence = presenceInspector.Inspect(audiobook, settings);
            if (diskPresence.HasFiles)
            {
                _logger.LogInformation(
                    "Audiobook '{Title}' already has {FileCount} audio file(s) on disk at {BasePath} (quality: {Quality})",
                    audiobook.Title, diskPresence.AudioFileCount, diskPresence.BasePath, diskPresence.BestQuality ?? "unrecognised");
            }

            // Check existing quality and decide whether to search
            var (cutoffMet, bestExistingQuality) = await _qualityEvaluator.GetExistingQualityAsync(
                audiobook, downloadRepository, fileRepository, diskPresence, stoppingToken);
            _logger.LogInformation("Audiobook '{Title}': cutoff met={CutoffMet}, best existing quality={BestQuality}",
                audiobook.Title, cutoffMet, bestExistingQuality ?? "none");

            // Files are present but none of them map to a rung in the profile, so there is no
            // quality to compare a candidate against. Grabbing here would duplicate what is already
            // on disk; the fix is to scan those files in, not to download the book twice.
            if (diskPresence.HasFiles && string.IsNullOrEmpty(bestExistingQuality))
            {
                _logger.LogWarning(
                    "Skipping automatic search for '{Title}': {FileCount} audio file(s) exist at {BasePath} but match no quality rung in profile '{Profile}'. Run a library scan so these files are tracked and can be evaluated for upgrades.",
                    audiobook.Title, diskPresence.AudioFileCount, diskPresence.BasePath, audiobook.QualityProfile.Name);
                return 0;
            }

            // Skip automatic search if quality cutoff is already met
            if (cutoffMet)
            {
                _logger.LogInformation("Quality cutoff already met for audiobook '{Title}', skipping automatic search", audiobook.Title);
                return 0;
            }

            // Build search query
            var searchQuery = _resultClassifier.BuildSearchQuery(audiobook);
            _logger.LogInformation("Searching for audiobook '{Title}' with query: {Query}", audiobook.Title, searchQuery);

            // Search for results
            var searchResults = await searchService.SearchAsync(searchQuery, isAutomaticSearch: true);
            _logger.LogInformation("Found {Count} raw search results for audiobook '{Title}'", searchResults.Count, audiobook.Title);

            // Broadcast detailed debug info about the raw search results to help diagnose automatic search failures
            try
            {
                // Build a concise summary of up to 10 raw results
                var rawSummaries = searchResults.Take(10).Select(r => new
                {
                    title = r.Title,
                    asin = r.Asin,
                    source = r.Source,
                    sizeMB = r.Size > 0 ? (r.Size / 1024 / 1024) : -1,
                    seeders = r.Seeders,
                    format = r.Format,
                    downloadType = r.DownloadType
                }).ToList();

                using var scope = _serviceScopeFactory.CreateScope();
                var hub = scope.ServiceProvider.GetRequiredService<IHubContext<DownloadHub>>();
                // Send structured payload with type and audiobookId so the UI can ignore automatic messages by default
                await hub.Clients.All.SendCoreAsync("SearchProgress", new object[] { new { message = $"Automatic search query: {searchQuery}", details = new { rawCount = searchResults.Count, rawSamples = rawSummaries }, type = "automatic", audiobookId = audiobook.Id } });
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogDebug(ex, "Failed to broadcast raw search results summary for audiobook {Id}", audiobook.Id);
            }

            if (!searchResults.Any())
            {
                _logger.LogInformation("No search results found for audiobook '{Title}'", audiobook.Title);
                return 0;
            }

            // Score results against quality profile
            var scoredResults = await qualityProfileService.ScoreSearchResults(searchResults, audiobook.QualityProfile);

            // Log all scored results for debugging
            _logger.LogInformation("Scored {Count} search results for audiobook '{Title}':", scoredResults.Count, audiobook.Title);

            // Broadcast scored result summaries (score + rejection reasons) to aid debugging
            try
            {
                var scoredSummaries = scoredResults.Select(s => new
                {
                    title = s.SearchResult.Title,
                    asin = s.SearchResult.Asin,
                    totalScore = s.TotalScore,
                    isRejected = s.IsRejected,
                    rejectionReasons = s.RejectionReasons,
                    source = s.SearchResult.Source,
                    sizeMB = s.SearchResult.Size > 0 ? (s.SearchResult.Size / 1024 / 1024) : -1,
                    seeders = s.SearchResult.Seeders,
                    format = s.SearchResult.Format
                }).ToList();

                using var scope2 = _serviceScopeFactory.CreateScope();
                var hub2 = scope2.ServiceProvider.GetRequiredService<IHubContext<DownloadHub>>();
                await hub2.Clients.All.SendCoreAsync("SearchProgress", new object[] { new { message = $"Scored results for '{audiobook.Title}'", details = new { scoredCount = scoredResults.Count, scoredSamples = scoredSummaries }, type = "automatic", audiobookId = audiobook.Id } });
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogDebug(ex, "Failed to broadcast scored search results for audiobook {Id}", audiobook.Id);
            }
            foreach (var scoredResult in scoredResults.OrderByDescending(s => s.TotalScore))
            {
                var status = scoredResult.IsRejected ? "REJECTED" : (scoredResult.TotalScore > 0 ? "ACCEPTABLE" : "LOW SCORE");
                _logger.LogInformation("  [{Status}] Score: {Score} | Title: {Title} | Source: {Source} | Size: {Size}MB | Seeders: {Seeders} | Quality: {Quality}",
                    status, scoredResult.TotalScore, scoredResult.SearchResult.Title, scoredResult.SearchResult.Source,
                    scoredResult.SearchResult.Size / 1024 / 1024, scoredResult.SearchResult.Seeders, scoredResult.SearchResult.Quality);

                if (scoredResult.IsRejected && scoredResult.RejectionReasons.Any())
                {
                    _logger.LogInformation("    Rejection reasons: {Reasons}", string.Join(", ", scoredResult.RejectionReasons));
                }
            }

            // Releases that already failed to download repeatedly are skipped rather than grabbed
            // again. Selection then falls through to the next best candidate, which is what makes
            // the library keep progressing instead of retrying one bad release forever.
            var blocklist = await blocklistService.GetForAudiobookAsync(audiobook.Id, stoppingToken);

            var eligibleResults = scoredResults
                .Where(s => !s.IsRejected)
                .Where(s => !IsBlocklisted(blocklist, s.SearchResult))
                .OrderByDescending(s => s.TotalScore)
                .ToList();

            var blocklistedCount = scoredResults.Count(s => !s.IsRejected) - eligibleResults.Count;
            if (blocklistedCount > 0)
            {
                _logger.LogInformation(
                    "Skipped {BlocklistedCount} blocklisted release(s) for audiobook '{Title}'",
                    blocklistedCount, audiobook.Title);
            }

            var topResult = eligibleResults.FirstOrDefault(); // Pick only the top scoring result

            if (topResult == null)
            {
                // Exhausted: every acceptable release for this book is blocklisted, so there is
                // nothing left to try until the blocklist is cleared or a new release appears.
                _logger.LogInformation(
                    "No acceptable search results found for audiobook '{Title}' after quality filtering{BlocklistNote}",
                    audiobook.Title,
                    blocklistedCount > 0 ? $" ({blocklistedCount} blocklisted)" : string.Empty);
                return 0;
            }

            _logger.LogInformation("Found top result for audiobook '{Title}': {ResultTitle} (Score: {Score}, Quality: {Quality})",
                audiobook.Title, topResult.SearchResult.Title, topResult.TotalScore, topResult.SearchResult.Quality);

            // Check if the found result is better quality than what we already have
            if (!string.IsNullOrEmpty(bestExistingQuality))
            {
                var resultIsBetter = _qualityEvaluator.IsQualityBetter(topResult.SearchResult.Quality, bestExistingQuality, audiobook.QualityProfile);
                if (!resultIsBetter)
                {
                    _logger.LogInformation("Top result quality '{ResultQuality}' is not better than existing quality '{ExistingQuality}' for audiobook '{Title}', skipping download",
                        topResult.SearchResult.Quality, bestExistingQuality, audiobook.Title);
                    return 0;
                }
                _logger.LogInformation("Top result quality '{ResultQuality}' is better than existing quality '{ExistingQuality}', proceeding with download",
                    topResult.SearchResult.Quality, bestExistingQuality);
            }
            else
            {
                _logger.LogInformation("No existing files for audiobook '{Title}', proceeding with download", audiobook.Title);
            }

            // Add score to the search result for tracking
            topResult.SearchResult.Score = topResult.TotalScore;

            // Queue download for the top result
            var downloadsQueued = 0;
            try
            {
                // Determine appropriate download client for this result
                var isTorrent = _resultClassifier.IsTorrentResult(topResult.SearchResult);
                var downloadClientId = await _downloadClientSelector.GetAppropriateDownloadClientAsync(topResult.SearchResult, isTorrent);

                if (string.IsNullOrEmpty(downloadClientId))
                {
                    _logger.LogWarning("No suitable download client found for result type: {Type}", isTorrent ? "torrent" : "NZB");
                    return 0;
                }

                await downloadService.StartDownloadAsync(topResult.SearchResult, downloadClientId, audiobook.Id);
                downloadsQueued++;

                _logger.LogInformation("Queued download for audiobook '{Title}': {ResultTitle} (Score: {Score})",
                    audiobook.Title, topResult.SearchResult.Title, topResult.TotalScore);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogError(ex, "Failed to queue download for audiobook '{Title}': {ResultTitle}",
                    audiobook.Title, topResult.SearchResult.Title);
            }

            return downloadsQueued;
        }

        private static bool IsBlocklisted(IReadOnlyList<BlocklistEntry> blocklist, SearchResult result) =>
            blocklist.Any(entry => entry.Matches(result.Id, result.IndexerId, result.Title, result.Size));
    }
}
