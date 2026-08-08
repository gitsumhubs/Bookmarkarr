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

using Bookmarkarr.Api.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Bookmarkarr.Api.Features.Prowlarr
{
    /// <summary>
    /// Detects and applies the AudioBook Bay pagination patch for Prowlarr.
    /// </summary>
    [ApiController]
    [Route("api/v{version:apiVersion}/indexers/audiobookbay")]
    [Tags("Indexers")]
    public class AudiobookBayPatchController : ControllerBase
    {
        private readonly AudiobookBayPatchWorkflow _workflow;
        private readonly ILogger<AudiobookBayPatchController> _logger;

        public AudiobookBayPatchController(
            AudiobookBayPatchWorkflow workflow,
            ILogger<AudiobookBayPatchController> logger)
        {
            _workflow = workflow;
            _logger = logger;
        }

        /// <summary>
        /// Report whether this instance searches AudioBook Bay through Prowlarr's single-page
        /// indexer, which caps every query at nine results.
        /// </summary>
        [HttpGet("patch")]
        public async Task<IActionResult> GetStatus(CancellationToken ct)
        {
            var status = await _workflow.GetStatusAsync(ct);
            return Ok(new
            {
                patchAvailable = status.PatchAvailable,
                reason = status.Reason,
                indexerId = status.IndexerId,
                indexerName = status.IndexerName,
                currentResultCap = status.CurrentResultCap,
                projectedResults = status.ProjectedResults,
                canAutoApply = status.CanAutoApply,
                definitionsDirectory = status.DefinitionsDirectory,
                defaultPages = AudiobookBayDefinition.DefaultPages,
                maximumPages = AudiobookBayDefinition.MaximumPages,
                resultsPerPage = AudiobookBayDefinition.ResultsPerPage
            });
        }

        /// <summary>
        /// Report every precondition the patch depends on, and whether it is currently in place.
        /// </summary>
        [HttpGet("patch/diagnostics")]
        public async Task<IActionResult> GetDiagnostics(CancellationToken ct)
        {
            var diagnostics = await _workflow.GetDiagnosticsAsync(ct);
            return Ok(Describe(diagnostics));
        }

        /// <summary>
        /// Check a candidate Prowlarr configuration directory before anything is written to it.
        /// </summary>
        [HttpPost("patch/probe-directory")]
        public async Task<IActionResult> ProbeDirectory([FromBody] AudiobookBayDirectoryProbeRequestDto? request, CancellationToken ct)
        {
            var probe = await _workflow.ProbeDirectoryAsync(request?.Path ?? string.Empty, ct);
            return Ok(new
            {
                path = probe.Path,
                exists = probe.Exists,
                looksLikeProwlarr = probe.LooksLikeProwlarr,
                writable = probe.Writable,
                message = probe.Message
            });
        }

        /// <summary>
        /// Search AudioBook Bay through Prowlarr to prove the site actually answers.
        /// </summary>
        [HttpPost("patch/probe-search")]
        public async Task<IActionResult> ProbeSearch([FromBody] AudiobookBaySearchProbeRequestDto? request, CancellationToken ct)
        {
            var probe = await _workflow.ProbeSearchAsync(request?.Query, ct);
            return Ok(new
            {
                ran = probe.Ran,
                results = probe.Results,
                query = probe.Query,
                message = probe.Message
            });
        }

        /// <summary>
        /// Put the indexer back on Prowlarr's compiled AudioBook Bay and remove what the patch added.
        /// </summary>
        [HttpPost("patch/revert")]
        public async Task<IActionResult> Revert([FromBody] AudiobookBayRevertRequestDto? request, CancellationToken ct)
        {
            var result = await _workflow.RevertAsync(request?.DeleteDefinition ?? false, ct);

            // Nothing recorded is a state, not a fault: the operator is told what to do by hand.
            if (result.Kind == AudiobookBayRevertResultKind.NothingRecorded)
            {
                return StatusCode(StatusCodes.Status409Conflict, new
                {
                    kind = "nothingRecorded",
                    message = result.Message
                });
            }

            if (result.Kind == AudiobookBayRevertResultKind.Failed)
            {
                return BadRequest(new { kind = "failed", message = result.Message });
            }

            _logger.LogInformation("Reverted the AudioBook Bay pagination patch on indexer {Id}", result.IndexerId);

            return Ok(new
            {
                kind = "success",
                indexerId = result.IndexerId,
                indexerName = result.IndexerName,
                restoredUrl = result.RestoredUrl,
                prowlarrIndexerDeleted = result.ProwlarrIndexerDeleted,
                definitionDeleted = result.DefinitionDeleted
            });
        }

        /// <summary>
        /// Install the paginated definition into Prowlarr and repoint this instance's indexer.
        /// </summary>
        [HttpPost("patch")]
        public async Task<IActionResult> Apply([FromBody] AudiobookBayPatchRequestDto? request, CancellationToken ct)
        {
            var pages = request?.Pages ?? AudiobookBayDefinition.DefaultPages;
            var result = await _workflow.ApplyAsync(
                pages,
                request?.DefinitionsDirectory,
                request?.DefinitionAlreadyInstalled ?? false,
                ct);

            // A missing mount is an expected configuration gap, not a fault: the caller falls back
            // to manual instructions, so it has to be distinguishable from a genuine failure.
            if (result.Kind == AudiobookBayPatchResultKind.MountRequired)
            {
                return StatusCode(StatusCodes.Status409Conflict, new
                {
                    kind = "mountRequired",
                    message = result.Message,
                    definitionFileName = AudiobookBayDefinition.FileName,
                    customSubdirectory = AudiobookBayDefinition.CustomSubdirectory
                });
            }

            if (result.Kind == AudiobookBayPatchResultKind.Failed)
            {
                return BadRequest(new { kind = "failed", message = result.Message });
            }

            _logger.LogInformation(
                "Applied the AudioBook Bay pagination patch: {Previous} -> {Projected} results",
                result.PreviousResultCap,
                result.ProjectedResults);

            return Ok(new
            {
                kind = "success",
                definitionPath = result.DefinitionPath,
                prowlarrIndexerId = result.ProwlarrIndexerId,
                indexerId = result.IndexerId,
                indexerName = result.IndexerName,
                pages = result.Pages,
                previousResultCap = result.PreviousResultCap,
                projectedResults = result.ProjectedResults
            });
        }

        /// <summary>
        /// Return the definition so it can be installed by hand when Prowlarr's config directory
        /// is not reachable from Bookmarkarr — the case for a Prowlarr on another host.
        /// </summary>
        [HttpGet("patch/definition")]
        public IActionResult DownloadDefinition([FromQuery] int? pages)
        {
            var yaml = AudiobookBayDefinition.Build(pages ?? AudiobookBayDefinition.DefaultPages);
            return File(System.Text.Encoding.UTF8.GetBytes(yaml), "application/yaml", AudiobookBayDefinition.FileName);
        }

        /// <summary>
        /// Return the definition as text, for an operator installing it by hand.
        /// </summary>
        [HttpGet("patch/definition/text")]
        public IActionResult ViewDefinition([FromQuery] int? pages)
        {
            return Ok(new
            {
                fileName = AudiobookBayDefinition.FileName,
                customSubdirectory = AudiobookBayDefinition.CustomSubdirectory,
                pages = Math.Clamp(pages ?? AudiobookBayDefinition.DefaultPages, AudiobookBayDefinition.MinimumPages, AudiobookBayDefinition.MaximumPages),
                yaml = AudiobookBayDefinition.Build(pages ?? AudiobookBayDefinition.DefaultPages)
            });
        }

        private static object Describe(AudiobookBayDiagnostics diagnostics) => new
        {
            patchState = diagnostics.PatchState.ToString(),
            checks = diagnostics.Checks.Select(check => new
            {
                id = check.Id,
                label = check.Label,
                state = check.State.ToString(),
                detail = check.Detail,
                remedy = check.Remedy
            }),
            indexerId = diagnostics.IndexerId,
            indexerName = diagnostics.IndexerName,
            prowlarrIndexerId = diagnostics.ProwlarrIndexerId,
            definitionsDirectory = diagnostics.DefinitionsDirectory,
            definitionsDirectoryFromOverride = diagnostics.DefinitionsDirectoryFromOverride,
            canAutoApply = diagnostics.CanAutoApply,
            canRevert = diagnostics.CanRevert,
            definitionInstalled = diagnostics.DefinitionInstalled,
            installedPages = diagnostics.InstalledPages,
            currentResultCap = diagnostics.CurrentResultCap,
            projectedResults = diagnostics.ProjectedResults,
            definitionFileName = AudiobookBayDefinition.FileName,
            customSubdirectory = AudiobookBayDefinition.CustomSubdirectory,
            defaultPages = AudiobookBayDefinition.DefaultPages,
            maximumPages = AudiobookBayDefinition.MaximumPages,
            resultsPerPage = AudiobookBayDefinition.ResultsPerPage
        };
    }
}
