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

namespace Bookmarkarr.Api.Features.Downloads
{
    public partial class DownloadController
    {
        /// <summary>
        /// List the filename mismatches detected for a download parked at Import Name Mismatch.
        /// </summary>
        /// <param name="downloadId">Download ID.</param>
        /// <param name="workflow">Resolved per-request.</param>
        [HttpGet("{downloadId}/import-issues")]
        public async Task<IActionResult> GetImportIssues(
            string downloadId,
            [FromServices] ImportNameFixWorkflow workflow)
        {
            return await workflow.GetIssuesAsync(downloadId);
        }

        /// <summary>
        /// Rename the mangled files to the names the client reports, then requeue the import.
        /// </summary>
        /// <param name="downloadId">Download ID.</param>
        /// <param name="request">Optional per-file corrections; omitted entries use the detected match.</param>
        /// <param name="workflow">Resolved per-request.</param>
        [HttpPost("{downloadId}/fix-import")]
        public async Task<IActionResult> FixImport(
            string downloadId,
            [FromBody] FixImportRequest? request,
            [FromServices] ImportNameFixWorkflow workflow)
        {
            return await workflow.ApplyAsync(downloadId, request?.Overrides);
        }
    }

    /// <summary>Operator corrections for a detected mismatch, keyed by the reported path.</summary>
    public sealed class FixImportRequest
    {
        public Dictionary<string, string>? Overrides { get; set; }
    }
}
