/*
 * Bookmarkarr - Audiobook Management System
 * Copyright (C) 2024-2026 Bookmarkarr Contributors
 */
using Microsoft.AspNetCore.Mvc;

namespace Bookmarkarr.Api.Features.Search
{
    /// <summary>
    /// The search queue's current state, in seconds, for a UI that has to explain a wait.
    /// </summary>
    /// <param name="MinimumCooldownSeconds">
    /// Shortest gap enforced between torrent requests. A countdown built from this is a floor —
    /// the real gap carries jitter on top — so the UI must never present it as an ETA.
    /// </param>
    /// <param name="CooldownRemainingSeconds">Seconds before the torrent lane opens again.</param>
    /// <param name="TorrentLaneBusy">True while a torrent request holds the lane.</param>
    /// <param name="TorrentRequestsWaiting">Torrent requests queued behind the one in progress.</param>
    /// <param name="BooksWaiting">Whole-book searches queued behind the one in progress.</param>
    public sealed record SearchThrottleStatusDto(
        double MinimumCooldownSeconds,
        double CooldownRemainingSeconds,
        bool TorrentLaneBusy,
        int TorrentRequestsWaiting,
        int BooksWaiting);

    public partial class SearchController : ControllerBase
    {
        /// <summary>
        /// Report how the search queue is currently paced.
        /// </summary>
        /// <remarks>
        /// Manual search fires one request per indexer and the torrent ones are held server-side,
        /// one at a time and at least a minute apart, so from the browser a paced search and a hung
        /// one look identical. This is what lets the modal show a countdown instead of a spinner.
        ///
        /// Literal route segments outrank the <c>{apiId}</c> parameter route in this controller, so
        /// this never shadows a search against an indexer actually named "throttle".
        /// </remarks>
        [HttpGet("throttle")]
        [ProducesResponseType(typeof(SearchThrottleStatusDto), StatusCodes.Status200OK)]
        public ActionResult<SearchThrottleStatusDto> GetThrottleStatus()
        {
            // Resolved here rather than through the constructor: every other dependency of this
            // controller is optional and constructor-injected, and one more parameter on a file
            // already close to the line cap buys nothing this single read needs.
            var snapshot = HttpContext.RequestServices.GetService<ISearchThrottle>()?.GetSnapshot();
            if (snapshot == null)
            {
                // Nothing is pacing searches, which is a real answer rather than an error: the UI
                // should stop showing a countdown, not show a failure.
                return Ok(new SearchThrottleStatusDto(0, 0, false, 0, 0));
            }

            return Ok(new SearchThrottleStatusDto(
                snapshot.MinimumCooldown.TotalSeconds,
                snapshot.CooldownRemaining.TotalSeconds,
                snapshot.TorrentLaneBusy,
                snapshot.TorrentRequestsWaiting,
                snapshot.BooksWaiting));
        }
    }
}
