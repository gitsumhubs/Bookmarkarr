/*
 * Bookmarkarr - Audiobook Management System
 * Copyright (C) 2024-2026 Bookmarkarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Microsoft.AspNetCore.Mvc;

namespace Bookmarkarr.Api.Features.Library;

/// <summary>
/// Adopts library folders Bookmarkarr has no record of, so books already on disk stop looking
/// missing — and stop being downloaded a second time when they are added.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/library/adopt")]
[Tags("Library")]
public sealed class LibraryAdoptionController(
    LibraryAdoptionWorkflow workflow,
    IRootFolderRepository rootFolderRepository,
    IAudiobookshelfClient audiobookshelfClient) : ControllerBase
{
    /// <summary>
    /// Plans, and optionally commits, adoption of a root folder.
    /// </summary>
    /// <param name="rootPath">Root to scan. Defaults to the first configured audiobook root.</param>
    /// <param name="dryRun">
    /// True (the default) reports what would happen without writing anything. Adoption creates
    /// records from heuristics over folder naming, so the preview is the point.
    /// </param>
    /// <param name="limit">Maximum folders to consider in one pass.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost]
    public async Task<ActionResult<AdoptionPlan>> Adopt(
        [FromQuery] string? rootPath,
        [FromQuery] bool dryRun = true,
        [FromQuery] int limit = 1000,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            var roots = await rootFolderRepository.GetAllAsync();
            rootPath = roots
                .FirstOrDefault(root => root.MediaType == EditionMediaType.Audiobook)?.Path;

            if (string.IsNullOrWhiteSpace(rootPath))
            {
                return BadRequest(new { error = "No audiobook root folder is configured" });
            }
        }

        try
        {
            return Ok(await workflow.PlanAsync(rootPath, dryRun, Math.Clamp(limit, 1, 10000), ct));
        }
        catch (DirectoryNotFoundException exception)
        {
            return NotFound(new { error = exception.Message });
        }
    }

    /// <summary>Checks whether Audiobookshelf is reachable and its token accepted.</summary>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("audiobookshelf/test")]
    public async Task<IActionResult> TestAudiobookshelf(CancellationToken ct)
    {
        var (success, message) = await audiobookshelfClient.TestConnectionAsync(ct);
        return success ? Ok(new { success, message }) : StatusCode(502, new { success, message });
    }
}
