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
using Bookmarkarr.Tests.Builders;

namespace Bookmarkarr.Tests.Features.Api.Features.Library
{
    public class LibraryController_AddToLibraryTests : BaseTests
    {
        private readonly Mock<IImageCacheService> imageCacheServiceMock = new Mock<IImageCacheService>();
        private readonly string imageUrl1 = "http://example.com/a1.jpg";
        private readonly string imageUrl2 = "http://example.com/a2.jpg";
        private string tempRoot = null!;

        public override async Task InitializeAsync()
        {
            imageCacheServiceMock.Setup(m => m.MoveToLibraryStorageAsync(It.IsAny<string>(), imageUrl1)).ReturnsAsync("config/cache/images/library/B000TEST01.jpg");
            imageCacheServiceMock.Setup(m => m.MoveToLibraryStorageAsync(It.IsAny<string>(), imageUrl2)).ReturnsAsync("config/cache/images/library/derived.jpg");

            Init(services => services.WithSingleton(imageCacheServiceMock.Object));
            await InitDataAsync();
        }

        private async Task InitDataAsync()
        {
            tempRoot = FileService.GetTempDirectory("bookmarkarr-test");

            await _applicationSettingsRepository.SaveAsync(new ApplicationSettingsBuilder()
                .WithFolderNamingPattern("{Author}")
                .WithFileNamingPattern("{Title}")
                .Build());

            await _rootFolderRepository.AddAsync(new RootFolderBuilder()
                .WithIsDefault()
                .WithPath(tempRoot)
                .Build());
        }

        [Fact]
        public async Task AddToLibrary_UsesLegacyAuthorField_PopulatesAuthorsAndBasePath()
        {
            var controller = _provider.GetRequiredService<LibraryController>();

            var request = new LibraryController.AddToLibraryRequest
            {
                Metadata = new AudibleBookMetadata
                {
                    Title = "Legacy Title",
                    Author = "Legacy Author"
                },
                Monitored = true
            };

            // Act
            var actionResult = await controller.AddToLibrary(request);

            // Assert
            Assert.IsType<OkObjectResult>(actionResult);

            var stored = (await _audiobookRepository.GetAllAsync()).First();
            Assert.NotNull(stored);
            Assert.NotNull(stored.Authors);
            Assert.Contains("Legacy Author", stored.Authors);
            Assert.Equal(Path.Join(tempRoot, "Legacy Author"), stored.BasePath);
        }

        [Fact]
        public async Task AddToLibrary_PersistsEditableMetadataFields()
        {
            var controller = _provider.GetRequiredService<LibraryController>();

            var request = new LibraryController.AddToLibraryRequest
            {
                Metadata = new AudibleBookMetadata
                {
                    Title = "Editable Title",
                    Subtitle = "Editable Subtitle",
                    Authors = new List<string> { "Edited Author" },
                    Narrators = new List<string> { "Edited Narrator" },
                    Publisher = "Edited Publisher",
                    Language = "english",
                    Runtime = 615,
                    Edition = "Collector's Edition",
                    Version = "Audible Version",
                    Asin = "B00EDIT123",
                    Isbn = new List<string> { "9781234567890" },
                    OpenLibraryId = "OL12345M"
                },
                Monitored = true
            };

            // Act
            var actionResult = await controller.AddToLibrary(request);

            // Assert
            Assert.IsType<OkObjectResult>(actionResult);

            var stored = (await _audiobookRepository.GetAllAsync()).First();
            Assert.NotNull(stored);
            Assert.Equal("Editable Title", stored.Title);
            Assert.Equal("Editable Subtitle", stored.Subtitle);
            Assert.Equal("Edited Publisher", stored.Publisher);
            Assert.Equal("english", stored.Language);
            Assert.Equal(615, stored.Runtime);
            Assert.Equal("Collector's Edition", stored.Edition);
            Assert.Equal("Audible Version", stored.Version);
            Assert.Equal("B00EDIT123", stored.Asin);
            Assert.Equal("OL12345M", stored.OpenLibraryId);
            Assert.NotNull(stored.Authors);
            Assert.Contains("Edited Author", stored.Authors);
            Assert.NotNull(stored.Narrators);
            Assert.Contains("Edited Narrator", stored.Narrators);
            Assert.NotNull(stored.Isbn);
            Assert.Contains("9781234567890", stored.Isbn);
        }

        [Fact]
        public async Task AddToLibrary_WithAsinRegion_PersistsIdentifierRegion()
        {
            var controller = _provider.GetRequiredService<LibraryController>();

            var request = new LibraryController.AddToLibraryRequest
            {
                Metadata = new AudibleBookMetadata
                {
                    Title = "Region Title",
                    Authors = new List<string> { "Region Author" },
                    Asin = "B00REGION1",
                    Region = "de"
                },
                Monitored = true
            };

            var actionResult = await controller.AddToLibrary(request);

            Assert.IsType<OkObjectResult>(actionResult);

            var stored = await _audiobookRepository.GetByAsinAsync("B00REGION1");
            Assert.NotNull(stored);
            var asinIdentifier = Assert.Single(
                stored.ExternalIdentifiers ?? new List<AudiobookExternalIdentifier>(),
                identifier => identifier.Type == AudiobookExternalIdentifierType.Asin);
            Assert.Equal("de", asinIdentifier.Region);
            Assert.True(asinIdentifier.IsPrimary);
        }

        [Fact]
        public async Task AddToLibrary_WithAsin_MovesImageToLibraryStorage()
        {
            var asin = "B000TEST01";

            // Configuration service providing an OutputPath root
            var tempRoot = FileService.GetTempDirectory("bookmarkarr-test");

            var controller = _provider.GetRequiredService<LibraryController>();

            var request = new LibraryController.AddToLibraryRequest
            {
                Metadata = new AudibleBookMetadata
                {
                    Title = "Move Test",
                    Author = "A Uthor",
                    Asin = "B000TEST01",
                    ImageUrl = imageUrl1
                },
                Monitored = true
            };

            // Act
            var actionResult = await controller.AddToLibrary(request);

            // Assert
            Assert.IsType<OkObjectResult>(actionResult);

            var stored = (await _audiobookRepository.GetAllAsync()).First();
            Assert.NotNull(stored);
            Assert.Equal($"/config/cache/images/library/B000TEST01.jpg", stored.ImageUrl);
            imageCacheServiceMock.Verify(m => m.MoveToLibraryStorageAsync(asin, imageUrl1), Times.Once);
        }

        [Fact]
        public async Task AddToLibrary_WithoutAsin_UsesDerivedKey_AndMovesImageToLibraryStorage()
        {
            // Configuration service providing an OutputPath root
            var tempRoot = FileService.GetTempDirectory("bookmarkarr-test");

            var controller = _provider.GetRequiredService<LibraryController>();

            var request = new LibraryController.AddToLibraryRequest
            {
                Metadata = new AudibleBookMetadata
                {
                    Title = "Derived Test",
                    Author = "Some Author",
                    ImageUrl = imageUrl2
                },
                Monitored = true
            };

            // Act
            var actionResult = await controller.AddToLibrary(request);

            // Assert
            Assert.IsType<OkObjectResult>(actionResult);

            var stored = (await _audiobookRepository.GetAllAsync()).First();
            Assert.NotNull(stored);
            Assert.Equal($"/config/cache/images/library/derived.jpg", stored.ImageUrl);
            imageCacheServiceMock.Verify(m => m.MoveToLibraryStorageAsync(It.IsAny<string>(), imageUrl2), Times.Once);
        }

        [Fact]
        public async Task AddToLibrary_WithCustomPath_StoresCustomPathAsBasePath()
        {
            var controller = _provider.GetRequiredService<LibraryController>();

            var customPath = "/custom/audiobooks/Author/Series/Title";
            var request = new LibraryController.AddToLibraryRequest
            {
                Metadata = new AudibleBookMetadata
                {
                    Title = "Custom Path Test",
                    Author = "Custom Author"
                },
                Monitored = true,
                DestinationPath = customPath  // Custom path provided
            };

            // Act
            var actionResult = await controller.AddToLibrary(request);

            // Assert
            Assert.IsType<OkObjectResult>(actionResult);

            var stored = (await _audiobookRepository.GetAllAsync()).First();
            Assert.NotNull(stored);
            // NormalizeStoredPath calls Path.GetFullPath which is platform-dependent:
            // on Windows "/custom/..." becomes "C:\custom\...", on Linux it stays "/custom/..."
            var expectedPath = Path.GetFullPath(customPath);
            Assert.Equal(expectedPath, stored.BasePath);
        }

        [Fact]
        public async Task AddToLibrary_HandlesWrongCustomPath()
        {
            var controller = _provider.GetRequiredService<LibraryController>();

            var customPath = "/custom/* ?|<>\0/Author/Series/Title";
            Assert.Throws<ArgumentException>(() => Path.GetFullPath(customPath));

            var request = new LibraryController.AddToLibraryRequest
            {
                Metadata = new AudibleBookMetadata
                {
                    Title = "Custom Path Test",
                    Author = "Custom Author"
                },
                Monitored = true,
                DestinationPath = customPath  // Custom path provided
            };

            // Act
            var actionResult = await controller.AddToLibrary(request);

            // Assert
            Assert.IsType<OkObjectResult>(actionResult);

            var stored = (await _audiobookRepository.GetAllAsync()).First();
            Assert.NotNull(stored);
            // Uses fallback logic with folder naming pattern
            Assert.Equal(Path.Join(tempRoot, "Custom Author"), stored.BasePath);
        }

        [Fact]
        public async Task AddToLibrary_WithoutRootFolderOrExplicitDestination_ReturnsBadRequestWithoutWritingBook()
        {
            foreach (var root in await _rootFolderRepository.GetAllAsync())
            {
                await _rootFolderRepository.RemoveAsync(root.Id);
            }

            var controller = _provider.GetRequiredService<LibraryController>();
            var result = await controller.AddToLibrary(new LibraryController.AddToLibraryRequest
            {
                Metadata = new AudibleBookMetadata { Title = "No Root", Author = "Example Author" },
                Monitored = true
            });

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("root folder", JsonSerializer.Serialize(badRequest.Value), StringComparison.OrdinalIgnoreCase);
            Assert.Empty(await _audiobookRepository.GetAllAsync());
        }

        [Fact]
        public async Task AddToLibrary_ResponseUsesNonCyclicDto_AndEditionTracksDefaultRoot()
        {
            var defaultRoot = await _rootFolderRepository.GetDefaultAsync();
            Assert.NotNull(defaultRoot);

            var controller = _provider.GetRequiredService<LibraryController>();
            var result = await controller.AddToLibrary(new LibraryController.AddToLibraryRequest
            {
                Metadata = new AudibleBookMetadata { Title = "Safe Response", Author = "Example Author" },
                Monitored = true,
                RootFolderId = defaultRoot.Id
            });

            var ok = Assert.IsType<OkObjectResult>(result);
            var responseAudiobook = ok.Value?.GetType().GetProperty("audiobook")?.GetValue(ok.Value);
            var responseDto = Assert.IsType<AudiobookDto>(responseAudiobook);
            var responseEdition = Assert.Single(responseDto.Editions);
            Assert.Equal(defaultRoot.Id, responseEdition.RootFolderId);
            var json = JsonSerializer.Serialize(ok.Value);
            Assert.Contains("Safe Response", json);

            var stored = Assert.Single(await _audiobookRepository.GetAllAsync());
            var edition = Assert.Single(stored.Editions);
            Assert.Equal(defaultRoot.Id, edition.RootFolderId);
        }

        [Fact]
        public async Task AddToLibrary_DestinationOutsideSelectedRoot_ReturnsBadRequest()
        {
            var defaultRoot = await _rootFolderRepository.GetDefaultAsync();
            Assert.NotNull(defaultRoot);
            var outsidePath = FileService.GetTempDirectory("bookmarkarr-outside-root");
            var controller = _provider.GetRequiredService<LibraryController>();

            var result = await controller.AddToLibrary(new LibraryController.AddToLibraryRequest
            {
                Metadata = new AudibleBookMetadata { Title = "Outside Root", Author = "Example Author" },
                DestinationPath = outsidePath,
                RootFolderId = defaultRoot.Id
            });

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("inside the selected root folder", JsonSerializer.Serialize(badRequest.Value));
            Assert.Empty(await _audiobookRepository.GetAllAsync());
        }
    }
}
