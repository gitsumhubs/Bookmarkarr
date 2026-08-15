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
using System.Net;
using System.Net.Http.Json;
using Asp.Versioning.ApiExplorer;
using Bookmarkarr.Tests.Mocks;

namespace Bookmarkarr.Tests.Features.Api.Features.Search
{
    /// <summary>
    /// The manual search modal polls this while a torrent search is queued, and shows a countdown
    /// built from what comes back. A route that answered anything else — or that got swallowed by
    /// the controller's <c>{apiId}</c> catch-all — would leave the user with a bare spinner again.
    /// </summary>
    public class SearchControllerThrottleTests : IClassFixture<BookmarkarrWebApplicationFactory>
    {
        private readonly BookmarkarrWebApplicationFactory _factory;

        public SearchControllerThrottleTests(BookmarkarrWebApplicationFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task ThrottleStatus_IsServedByItsOwnRouteRatherThanTheIndexerCatchAll()
        {
            var apiBasePath = ResolveApiBasePath(_factory.Services);
            var client = _factory.CreateClient();

            var response = await client.GetAsync($"{apiBasePath}/search/throttle");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var status = await response.Content.ReadFromJsonAsync<SearchThrottleStatusDto>();

            Assert.NotNull(status);

            // The shipped pacing, which is what the countdown's wording is built from. Reading zero
            // here would mean the modal quietly stopped explaining the wait.
            Assert.Equal(60, status!.MinimumCooldownSeconds);
            Assert.True(status.CooldownRemainingSeconds >= 0);
            Assert.True(status.TorrentRequestsWaiting >= 0);
            Assert.True(status.BooksWaiting >= 0);
        }

        private static string ResolveApiBasePath(IServiceProvider services)
        {
            using var scope = services.CreateScope();
            var provider = scope.ServiceProvider.GetService<IApiVersionDescriptionProvider>();
            var groupName = provider?.ApiVersionDescriptions.FirstOrDefault(d => !d.IsDeprecated)?.GroupName
                ?? provider?.ApiVersionDescriptions.FirstOrDefault()?.GroupName;

            return string.IsNullOrWhiteSpace(groupName) ? "/api/v1" : $"/api/{groupName}";
        }
    }
}
