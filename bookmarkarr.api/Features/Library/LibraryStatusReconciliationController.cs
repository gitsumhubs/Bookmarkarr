/* Bookmarkarr is licensed under the GNU AGPL v3 or later. */
using Microsoft.AspNetCore.Mvc;

namespace Bookmarkarr.Api.Features.Library;

[ApiController]
[Route("api/v{version:apiVersion}/library/status")]
[Tags("Library")]
public sealed class LibraryStatusReconciliationController(
    LibraryStatusReconciliationWorkflow workflow) : ControllerBase
{
    /// <summary>
    /// Reconciles edition wanted states with registered files and fresh download-client
    /// snapshots. Dry-run is the safe default.
    /// </summary>
    [HttpPost("reconcile")]
    public async Task<ActionResult<LibraryStatusReconciliationResult>> Reconcile(
        [FromQuery] bool dryRun = true,
        CancellationToken ct = default)
        => Ok(await workflow.ReconcileAsync(dryRun, ct));
}
