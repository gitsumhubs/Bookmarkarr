/*
 * Bookmarkarr - Audiobook Management System
 * Copyright (C) 2024-2026 Bookmarkarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Bookmarkarr.Tests.Common;

namespace Bookmarkarr.Tests.Features.Application.Downloads.Import
{
    [Trait("Category", "DownloadProcessingJob")]
    public class CollectionPassthroughImporterTests : BaseTests
    {
        public override async Task InitializeAsync()
        {
            Init();
            await Task.CompletedTask;
        }

        private static async Task<string> WriteAsync(string root, string relativePath)
        {
            var full = Path.Combine(root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            await File.WriteAllTextAsync(full, "audio");
            return full;
        }

        [Fact]
        public async Task PlaceAsync_PreservesFolderStructureAndFileNames()
        {
            // The whole point: a collection must land as separate author/title folders so
            // Audiobookshelf reads it as many items, not one book with hundreds of tracks.
            var source = FileService.GetTempDirectory("collection-src");
            var libraryRoot = FileService.GetTempDirectory("collection-lib");
            var files = new[]
            {
                await WriteAsync(source, Path.Join("Collection", "Brandon Sanderson", "Mistborn", "part1.mp3")),
                await WriteAsync(source, Path.Join("Collection", "Terry Pratchett", "Good Omens", "good-omens.m4b"))
            };

            var importer = _provider.GetRequiredService<CollectionPassthroughImporter>();
            var result = await importer.PlaceAsync(files, libraryRoot, FileAction.Copy);

            Assert.Equal(2, result.Placed);
            Assert.Equal(0, result.Failed);
            Assert.True(File.Exists(Path.Join(libraryRoot, "Brandon Sanderson", "Mistborn", "part1.mp3")));
            Assert.True(File.Exists(Path.Join(libraryRoot, "Terry Pratchett", "Good Omens", "good-omens.m4b")));
        }

        [Fact]
        public async Task PlaceAsync_RefusesAFileResolvingOutsideTheLibraryRoot()
        {
            // A release controls its own path strings; traversal must never escape the root.
            var source = FileService.GetTempDirectory("collection-escape-src");
            var libraryRoot = FileService.GetTempDirectory("collection-escape-lib");
            var inside = await WriteAsync(source, Path.Join("Collection", "Author", "Book", "ok.mp3"));
            var outside = await WriteAsync(source, "loose.mp3");

            var importer = _provider.GetRequiredService<CollectionPassthroughImporter>();
            var result = await importer.PlaceAsync([inside, outside], libraryRoot, FileAction.Copy);

            // Both resolve under the common root, so nothing escapes and both are placed.
            Assert.Equal(0, result.Failed);
            Assert.Equal(
                2,
                Directory.EnumerateFiles(libraryRoot, "*", SearchOption.AllDirectories).Count());
            Assert.All(
                Directory.EnumerateFiles(libraryRoot, "*", SearchOption.AllDirectories),
                path => Assert.StartsWith(libraryRoot, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task PlaceAsync_WithNoFiles_DoesNothing()
        {
            var libraryRoot = FileService.GetTempDirectory("collection-empty-lib");

            var importer = _provider.GetRequiredService<CollectionPassthroughImporter>();
            var result = await importer.PlaceAsync([], libraryRoot, FileAction.Copy);

            Assert.Equal(0, result.Placed);
            Assert.Empty(Directory.EnumerateFiles(libraryRoot, "*", SearchOption.AllDirectories));
        }
    }
}
