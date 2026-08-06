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

using Microsoft.AspNetCore.Mvc;

namespace Bookmarkarr.Api.Features.Downloads;

/// <summary>
/// Releases that failed to download often enough that automatic search stops picking them.
/// Removing an entry makes that release eligible again on the next search.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/blocklist")]
[Tags("Blocklist")]
public class BlocklistController(
    IBlocklistRepository blocklistRepository,
    ILogger<BlocklistController> logger) : ControllerBase
{
    /// <summary>All blocklisted releases, most recently updated first.</summary>
    [HttpGet]
    public async Task<ActionResult<List<BlocklistEntry>>> GetAll(CancellationToken ct) =>
        Ok(await blocklistRepository.GetAllAsync(ct));

    /// <summary>Blocklisted releases for one book.</summary>
    /// <param name="audiobookId">Book identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("audiobook/{audiobookId:int}")]
    public async Task<ActionResult<List<BlocklistEntry>>> GetByAudiobook(int audiobookId, CancellationToken ct) =>
        Ok(await blocklistRepository.GetByAudiobookIdAsync(audiobookId, ct));

    /// <summary>
    /// Reports which of the supplied releases are blocklisted for a book.
    ///
    /// Exists so manual search can mark blocklisted results without reimplementing
    /// <see cref="ReleaseIdentity"/> in the frontend, where it would drift from the matching
    /// automatic search actually uses.
    /// </summary>
    /// <param name="request">Book identifier and the candidate releases to check.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The <c>releaseId</c> values that are blocklisted.</returns>
    [HttpPost("check")]
    public async Task<ActionResult<BlocklistCheckResponse>> Check(
        [FromBody] BlocklistCheckRequest request,
        CancellationToken ct)
    {
        if (request.Releases is null || request.Releases.Count == 0)
        {
            return Ok(new BlocklistCheckResponse([]));
        }

        var entries = await blocklistRepository.GetByAudiobookIdAsync(request.AudiobookId, ct);
        if (entries.Count == 0)
        {
            return Ok(new BlocklistCheckResponse([]));
        }

        var blocked = request.Releases
            .Where(release => entries.Any(entry =>
                entry.Matches(release.ReleaseId, release.IndexerId, release.Title, release.Size)))
            .Select(release => release.ReleaseId)
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Ok(new BlocklistCheckResponse(blocked));
    }

    /// <summary>Removes one entry, making that release eligible again.</summary>
    /// <param name="id">Blocklist entry identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) =>
        await blocklistRepository.DeleteAsync(id, ct)
            ? NoContent()
            : NotFound(new { error = $"Blocklist entry {id} not found" });

    /// <summary>Clears every entry for a book so a retry starts from a clean slate.</summary>
    /// <param name="audiobookId">Book identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpDelete("audiobook/{audiobookId:int}")]
    public async Task<IActionResult> DeleteByAudiobook(int audiobookId, CancellationToken ct)
    {
        var removed = await blocklistRepository.DeleteByAudiobookIdAsync(audiobookId, ct);
        logger.LogInformation("Cleared {Removed} blocklist entries for audiobook {AudiobookId}", removed, audiobookId);
        return Ok(new { removed });
    }
}

/// <summary>One release to test against a book's blocklist.</summary>
/// <param name="ReleaseId">Identifier the caller uses for this result; echoed back when blocklisted.</param>
/// <param name="IndexerId">Indexer the release came from, when known.</param>
/// <param name="Title">Release name exactly as the indexer reported it.</param>
/// <param name="Size">Reported size in bytes.</param>
public sealed record BlocklistCheckCandidate(string ReleaseId, int? IndexerId, string? Title, long Size);

/// <summary>Blocklist check request for one book.</summary>
/// <param name="AudiobookId">Book whose blocklist to test against.</param>
/// <param name="Releases">Candidate releases.</param>
public sealed record BlocklistCheckRequest(int AudiobookId, List<BlocklistCheckCandidate> Releases);

/// <summary>Blocklist check result.</summary>
/// <param name="BlocklistedReleaseIds">The supplied release ids that are blocklisted.</param>
public sealed record BlocklistCheckResponse(List<string> BlocklistedReleaseIds);
