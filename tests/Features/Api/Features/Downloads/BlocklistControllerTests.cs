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
using Microsoft.Extensions.Logging.Abstractions;

namespace Bookmarkarr.Tests.Features.Api.Features.Downloads
{
    public class BlocklistControllerTests
    {
        private const int AudiobookId = 42;
        private const string ReleaseTitle = "An.Author-Example.Book.2024.M4B-GROUP";

        [Fact]
        public async Task Check_FlagsAReleaseMatchingByGuid()
        {
            var controller = CreateController([Entry(releaseGuid: "guid-1")]);

            var result = await Check(controller, new BlocklistCheckCandidate("guid-1", 3, ReleaseTitle, 500_000_000));

            Assert.Equal(["guid-1"], result.BlocklistedReleaseIds);
        }

        [Fact]
        public async Task Check_FlagsAReleaseMatchingByTitleAndSize_WhenNoGuidIsAvailable()
        {
            // Plenty of indexers omit a GUID or rotate it between queries, so the fallback has to
            // work or those releases would never be recognised as blocklisted.
            var controller = CreateController([Entry(releaseGuid: null)]);

            var result = await Check(controller, new BlocklistCheckCandidate("some-id", 3, ReleaseTitle, 500_000_000));

            Assert.Equal(["some-id"], result.BlocklistedReleaseIds);
        }

        [Fact]
        public async Task Check_LeavesADifferentReleaseAlone()
        {
            var controller = CreateController([Entry(releaseGuid: "guid-1")]);

            var result = await Check(
                controller,
                new BlocklistCheckCandidate("guid-2", 3, "An.Author-Example.Book.2024.MP3-OTHER", 120_000_000));

            Assert.Empty(result.BlocklistedReleaseIds);
        }

        [Fact]
        public async Task Check_LeavesTheSameReleaseOnADifferentIndexerAlone()
        {
            // Blocklisting is per indexer: the same name elsewhere may be a perfectly good post.
            var controller = CreateController([Entry(releaseGuid: null)]);

            var result = await Check(controller, new BlocklistCheckCandidate("x", 99, ReleaseTitle, 500_000_000));

            Assert.Empty(result.BlocklistedReleaseIds);
        }

        [Fact]
        public async Task Check_ReturnsEmpty_WhenTheBookHasNoBlocklist()
        {
            var controller = CreateController([]);

            var result = await Check(controller, new BlocklistCheckCandidate("guid-1", 3, ReleaseTitle, 500_000_000));

            Assert.Empty(result.BlocklistedReleaseIds);
        }

        [Fact]
        public async Task Check_ReturnsEmpty_ForAnEmptyRequest()
        {
            var controller = CreateController([Entry(releaseGuid: "guid-1")]);

            var response = await controller.Check(new BlocklistCheckRequest(AudiobookId, []), CancellationToken.None);

            Assert.Empty(Unwrap(response).BlocklistedReleaseIds);
        }

        private static async Task<BlocklistCheckResponse> Check(
            BlocklistController controller,
            params BlocklistCheckCandidate[] candidates)
        {
            var response = await controller.Check(
                new BlocklistCheckRequest(AudiobookId, [.. candidates]), CancellationToken.None);
            return Unwrap(response);
        }

        private static BlocklistCheckResponse Unwrap(ActionResult<BlocklistCheckResponse> response) =>
            (BlocklistCheckResponse)((OkObjectResult)response.Result!).Value!;

        private static BlocklistEntry Entry(string? releaseGuid) => new()
        {
            Id = 1,
            AudiobookId = AudiobookId,
            ReleaseGuid = releaseGuid,
            IndexerId = 3,
            Title = ReleaseTitle,
            NormalizedTitle = ReleaseIdentity.NormalizeReleaseName(ReleaseTitle),
            Size = 500_000_000
        };

        private static BlocklistController CreateController(List<BlocklistEntry> entries)
        {
            var repository = new Mock<IBlocklistRepository>();
            repository
                .Setup(repo => repo.GetByAudiobookIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(entries);

            return new BlocklistController(repository.Object, NullLogger<BlocklistController>.Instance);
        }
    }
}
