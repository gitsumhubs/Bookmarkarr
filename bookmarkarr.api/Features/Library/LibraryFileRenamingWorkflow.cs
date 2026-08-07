/*
 * Bookmarkarr - Audiobook Management System
 * Copyright (C) 2024-2026 Bookmarkarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Bookmarkarr.Application.Audiobooks.Contracts.Repositories;
using Bookmarkarr.Domain.Audiobooks;
using Microsoft.AspNetCore.Mvc;

namespace Bookmarkarr.Api.Features.Library
{
    /// <summary>
    /// Sets, or clears, a single book's override of the library-wide file renaming default.
    /// </summary>
    /// <remarks>
    /// Deliberately separate from the audiobook <c>PUT</c>, which binds a whole
    /// <see cref="Audiobook"/> and assigns fields unconditionally. The override is a tri-state —
    /// true, false, or "inherit" — and a partial body sent to that endpoint deserializes the
    /// missing field as null, which is itself a meaningful value here. Routing through the full
    /// update would therefore silently reset a book's choice every time an unrelated field
    /// (monitored, say) was saved from another screen.
    /// </remarks>
    public sealed class LibraryFileRenamingWorkflow(
        IAudiobookRepository repository,
        ILogger<LibraryFileRenamingWorkflow> logger)
    {
        public async Task<IActionResult> SetOverrideAsync(int audiobookId, bool? disableRenaming)
        {
            var audiobook = await repository.GetByIdAsync(audiobookId);
            if (audiobook == null)
            {
                return new NotFoundObjectResult(new { message = $"Audiobook {audiobookId} not found" });
            }

            audiobook.DisableFileRenamingOverride = disableRenaming;
            await repository.UpdateAsync(audiobook);

            logger.LogInformation(
                "File renaming override for audiobook {AudiobookId} set to {Override}",
                audiobookId,
                disableRenaming?.ToString() ?? "inherit");

            return new OkObjectResult(new
            {
                message = "File renaming preference updated",
                audiobookId,
                disableFileRenamingOverride = disableRenaming
            });
        }
    }

    /// <summary>Request body for setting a book's renaming override. Null clears it.</summary>
    public sealed class FileRenamingOverrideRequest
    {
        /// <summary>True keeps original filenames, false forces renaming, null inherits the library default.</summary>
        public bool? DisableRenaming { get; set; }
    }
}
