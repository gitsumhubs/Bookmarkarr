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

namespace Bookmarkarr.Api.Features.Library
{
    public partial class LibraryController
    {
        /// <summary>
        /// Set or clear one book's override of the library-wide file renaming default.
        /// </summary>
        /// <param name="id">Audiobook ID.</param>
        /// <param name="request">True keeps original filenames, false forces renaming, null inherits.</param>
        /// <param name="workflow">Resolved per-request.</param>
        /// <remarks>
        /// Injected at the action rather than through the constructor: the controller is built
        /// directly by a large number of tests, and widening its constructor breaks every one of
        /// them for a dependency only this endpoint uses.
        /// </remarks>
        [HttpPut("{id}/file-renaming")]
        public async Task<IActionResult> SetFileRenamingOverride(
            int id,
            [FromBody] FileRenamingOverrideRequest request,
            [FromServices] LibraryFileRenamingWorkflow workflow)
        {
            return await workflow.SetOverrideAsync(id, request?.DisableRenaming);
        }
    }
}
