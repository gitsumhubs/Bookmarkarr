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
