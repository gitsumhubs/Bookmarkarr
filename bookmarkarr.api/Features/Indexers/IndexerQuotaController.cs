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

namespace Bookmarkarr.Api.Features.Indexers
{
    /// <summary>
    /// Reports the hourly request budget each indexer is working within.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="IndexersController"/> because an indexer's budget is consulted on a
    /// timer by the UI while the rest of that controller is not, and because the file was already
    /// at the size the architecture tests allow.
    /// </remarks>
    [ApiController]
    [Route("api/v{version:apiVersion}/indexers")]
    [Tags("Indexers")]
    public class IndexerQuotaController : ControllerBase
    {
        private readonly IIndexerRepository _indexerRepository;
        private readonly IIndexerQuotaService _quotaService;

        public IndexerQuotaController(IIndexerRepository indexerRepository, IIndexerQuotaService quotaService)
        {
            _indexerRepository = indexerRepository;
            _quotaService = quotaService;
        }

        /// <summary>
        /// Every indexer's budget: what is left, and when the next request frees up.
        /// </summary>
        /// <remarks>
        /// Exposed so a rationed indexer is visible rather than mysterious. Searches that quietly
        /// return nothing because the budget is spent look exactly like a library with nothing to
        /// find, which is the confusion this endpoint exists to prevent.
        /// </remarks>
        [HttpGet("quota")]
        public async Task<IActionResult> GetQuotas(CancellationToken ct)
        {
            var indexers = await _indexerRepository.GetAllAsync(ct);
            var states = new List<object>(indexers.Count);

            foreach (var indexer in indexers)
            {
                var state = await _quotaService.GetStateAsync(indexer.Id, ct);
                states.Add(new
                {
                    indexerId = state.IndexerId,
                    indexerName = state.IndexerName,
                    isLimited = state.IsLimited,
                    requestsPerHour = state.RequestsPerHour,
                    used = state.Used,
                    remaining = state.IsLimited ? state.Remaining : (int?)null,
                    interactiveReserve = state.InteractiveReserve,
                    automaticRemaining = state.IsLimited ? state.AutomaticRemaining : (int?)null,
                    nextSlotUtc = state.NextSlotUtc,
                    windowEndsUtc = state.WindowEndsUtc
                });
            }

            return Ok(states);
        }
    }
}
