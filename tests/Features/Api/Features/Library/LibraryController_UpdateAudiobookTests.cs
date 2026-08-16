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
using System.Text.Json;
using Bookmarkarr.Application.Audiobooks.Common;
using Bookmarkarr.Tests.Common;

namespace Bookmarkarr.Tests.Features.Api.Features.Library
{
    [Trait("Name", "LibraryController_UpdateAudiobookTests")]
    [Trait("Category", "Library")]
    public class LibraryController_UpdateAudiobookTests : BaseTests
    {
        [Fact]
        [Trait("Scenario", "The update response serializes, having committed its write")]
        public async Task UpdateAudiobook_ResponseUsesNonCyclicDto()
        {
            // Regression: BF-012 replaced the entity graph with a DTO on the add and
            // duplicate-conflict responses but left the update endpoint returning the tracked
            // entity. Audiobook -> Editions -> Book -> Editions then recursed past the serializer
            // depth limit after the write had already committed, so the caller saw a 500 for a
            // change that had persisted — the worst shape a write failure can take.
            var audiobook = await CreateAudiobook();

            var controller = _provider.GetRequiredService<LibraryController>();
            var result = await controller.UpdateAudiobook(audiobook.Id, new Audiobook
            {
                Id = audiobook.Id,
                Title = "Renamed Title",
                Monitored = true
            });

            var ok = Assert.IsType<OkObjectResult>(result);
            var responseAudiobook = ok.Value?.GetType().GetProperty("audiobook")?.GetValue(ok.Value);
            Assert.IsType<AudiobookDto>(responseAudiobook);

            // Serializing is the actual assertion; the cycle threw here before the fix.
            var json = JsonSerializer.Serialize(ok.Value);
            Assert.Contains("Renamed Title", json, StringComparison.Ordinal);
        }

        [Fact]
        [Trait("Scenario", "A blank BasePath is a blank form field, not a request to unset the folder")]
        public async Task UpdateAudiobook_WithEmptyBasePath_KeepsTheStoredValue()
        {
            // Taking an empty string literally stored a path that resolves to nothing and quietly
            // broke organisation and import for the book, with only an "[empty-path]" line in the
            // log to show for it.
            var audiobook = await CreateAudiobook();
            audiobook.BasePath = "/library/audiobooks/Example Author/Example Book";
            await _audiobookRepository.UpdateAsync(audiobook);

            var controller = _provider.GetRequiredService<LibraryController>();
            var result = await controller.UpdateAudiobook(audiobook.Id, new Audiobook
            {
                Id = audiobook.Id,
                Title = audiobook.Title,
                BasePath = "   "
            });

            Assert.IsType<OkObjectResult>(result);

            var stored = await _audiobookRepository.GetByIdAsync(audiobook.Id);
            Assert.Equal("/library/audiobooks/Example Author/Example Book", stored!.BasePath);
        }
    }
}
