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

using Bookmarkarr.Application.Common;
using Bookmarkarr.Application.Search.Indexers.Common;
using Microsoft.Extensions.Logging;

namespace Bookmarkarr.Application.Downloads.Submission
{
    public partial class DownloadService
    {
        public async Task<SearchAndDownloadResult> SearchAndDownloadAsync(int audiobookId)
        {
            // Get the audiobook
            var audiobook = await audiobookRepository.GetByIdAsync(audiobookId);
            if (audiobook == null)
            {
                return new SearchAndDownloadResult
                {
                    Success = false,
                    Message = "Audiobook not found"
                };
            }

            // The edition owns the quality profile in Bookmarkarr's model; the book-level
            // one is the legacy field. Fall back to the audiobook edition so a book whose
            // parent row predates edition-aware defaults can still search.
            var qualityProfile = audiobook.QualityProfile
                ?? audiobook.Editions?
                    .FirstOrDefault(edition => edition.MediaType == EditionMediaType.Audiobook
                        && edition.QualityProfile != null)?.QualityProfile;

            if (qualityProfile == null)
            {
                logger.LogWarning(
                    "Book '{Title}' has no quality profile on the book or its audiobook edition",
                    audiobook.Title);
                return new SearchAndDownloadResult
                {
                    Success = false,
                    Message = "No quality profile assigned to this book or its audiobook edition"
                };
            }

            // Same queue the automatic pass uses, so a search started here — the Wanted page, a
            // catalogue import, a retry after a failed download — cannot run alongside one, and the
            // book after it waits out the cooldown. Held for every rung below, not per rung.
            var torrentIndexerActive = IndexerProtocol.AnyTorrent(await indexerRepository.GetEnabledAsync(true));
            using var searchLease = await searchThrottle.AcquireBookAsync(audiobook.Title, torrentIndexerActive);

            // Progressively broader queries. Catalogue titles carry series suffixes,
            // numbering, and punctuation that indexers do not, so a single literal query
            // can return nothing for a book a broader search finds immediately. The first
            // query to return anything wins, so a precise match is still preferred.
            var searchCandidates = DownloadSearchQueryBuilder.BuildCandidates(audiobook);
            List<SearchResult>? searchResults = null;

            foreach (var searchQuery in searchCandidates)
            {
                logger.LogInformation(
                    "Searching for audiobook '{Title}' with query: {Query}",
                    LogRedaction.SanitizeText(audiobook.Title),
                    LogRedaction.SanitizeText(searchQuery));

                // Automatic search (background/'search-and-download'), so only indexers are
                // queried - no Amazon/Audible scraping.
                var attempt = await searchService.SearchAsync(searchQuery, isAutomaticSearch: true);
                if (attempt != null && attempt.Count > 0)
                {
                    searchResults = attempt;
                    break;
                }

                logger.LogInformation(
                    "No results for query '{Query}'; trying a broader query if one remains",
                    LogRedaction.SanitizeText(searchQuery));
            }

            if (searchResults == null || searchResults.Count == 0)
            {
                return new SearchAndDownloadResult
                {
                    Success = false,
                    Message = "No search results found"
                };
            }

            // Score results against quality profile
            var scoredResults = await qualityProfileService.ScoreSearchResults(searchResults, qualityProfile);

            // Log all scored results for debugging
            logger.LogInformation("Scored {Count} search results for audiobook '{Title}':", scoredResults.Count, LogRedaction.SanitizeText(audiobook.Title));
            foreach (var scoredResult in scoredResults.OrderByDescending(s => s.TotalScore))
            {
                var status = scoredResult.IsRejected ? "REJECTED" : (scoredResult.TotalScore > 0 ? "ACCEPTABLE" : "LOW SCORE");
                logger.LogInformation("  [{Status}] Score: {Score} | Title: {Title} | Source: {Source} | Size: {Size}MB | Seeders: {Seeders} | Quality: {Quality}",
                    status, scoredResult.TotalScore, LogRedaction.SanitizeText(scoredResult.SearchResult.Title), LogRedaction.SanitizeText(scoredResult.SearchResult.Source),
                    scoredResult.SearchResult.Size / 1024 / 1024, scoredResult.SearchResult.Seeders, scoredResult.SearchResult.Quality);
                if (scoredResult.IsRejected && scoredResult.RejectionReasons.Any())
                {
                    logger.LogInformation("    Rejection reasons: {Reasons}", string.Join(", ", scoredResult.RejectionReasons));
                }
            }

            // Only consider non-rejected, score > 0 results
            var topResult = scoredResults
                .Where(s => !s.IsRejected && s.TotalScore > 0)
                .OrderByDescending(s => s.TotalScore)
                .FirstOrDefault();

            if (topResult == null)
            {
                logger.LogWarning("No acceptable search results found for audiobook '{Title}' after quality filtering", audiobook.Title);
                return new SearchAndDownloadResult
                {
                    Success = false,
                    Message = "No acceptable search results found"
                };
            }

            // Assign score to SearchResult
            topResult.SearchResult.Score = topResult.TotalScore;

            var candidate = TrustedDownloadCandidateFactory.Create(topResult.SearchResult);
            var isTorrent = candidate.SourceDescriptor.Protocol == DownloadProtocol.Torrent;
            var downloadClientId = await downloadClientSelector.GetAppropriateDownloadClientAsync(isTorrent);

            if (downloadClientId == null)
            {
                logger.LogWarning("No suitable download client found for type: {Type}", isTorrent ? "Torrent" : "NZB");
                return new SearchAndDownloadResult
                {
                    Success = false,
                    Message = $"No suitable download client found for {(isTorrent ? "torrent" : "NZB")} results"
                };
            }

            // Send to download client with audiobookId for proper metadata linking
            var downloadId2 = await SendToDownloadClientAsync(candidate, downloadClientId, audiobookId);

            // Log to history
            await LogDownloadHistory(audiobook, "Search", topResult.SearchResult);

            return new SearchAndDownloadResult
            {
                Success = true,
                Message = $"Successfully sent to download client",
                DownloadId = downloadId2,
                IndexerUsed = "Search",
                DownloadClientUsed = downloadClientId,
                SearchResult = topResult.SearchResult
            };
        }
    }
}
