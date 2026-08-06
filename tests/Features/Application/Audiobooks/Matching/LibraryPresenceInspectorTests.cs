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

using Bookmarkarr.Domain.Books;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bookmarkarr.Tests.Features.Application.Audiobooks.Matching
{
    public class LibraryPresenceInspectorTests
    {
        private const string Root = "/audiobooks";
        private const string BookPath = "/audiobooks/Amor Towles/A Gentleman in Moscow";

        [Fact]
        public void Inspect_ReportsFiles_WhenBookDirectoryHoldsAudio()
        {
            var inspector = CreateInspector(
                directories: [BookPath],
                files: [$"{BookPath}/A Gentleman in Moscow.m4b"]);

            var presence = inspector.Inspect(CreateAudiobook(), CreateSettings());

            Assert.True(presence.HasFiles);
            Assert.Equal(1, presence.AudioFileCount);
            Assert.Equal(BookPath, presence.BasePath);
        }

        [Fact]
        public void Inspect_ReportsNothing_WhenBookDirectoryDoesNotExist()
        {
            var inspector = CreateInspector(directories: [], files: []);

            var presence = inspector.Inspect(CreateAudiobook(), CreateSettings());

            Assert.False(presence.HasFiles);
            Assert.Equal(0, presence.AudioFileCount);
        }

        [Fact]
        public void Inspect_IgnoresNonAudioFiles()
        {
            // A book folder holding only artwork and an ebook has no audio, so an audio grab must
            // still be allowed to proceed.
            var inspector = CreateInspector(
                directories: [BookPath],
                files: [$"{BookPath}/cover.jpg", $"{BookPath}/metadata.json", $"{BookPath}/book.epub"]);

            var presence = inspector.Inspect(CreateAudiobook(), CreateSettings());

            Assert.False(presence.HasFiles);
        }

        [Fact]
        public void Inspect_ReportsNothing_WhenPathCollapsesToLibraryRoot()
        {
            // Guard against a mis-derived path: scanning the root would report every book in the
            // library as already present for whichever single book is being evaluated.
            var audiobook = CreateAudiobook();
            audiobook.BasePath = Root;

            var inspector = CreateInspector(
                directories: [Root],
                files: [$"{Root}/Someone Else/Another Book/another.m4b"]);

            var presence = inspector.Inspect(audiobook, CreateSettings());

            Assert.False(presence.HasFiles);
        }

        [Fact]
        public void Inspect_ResolvesBestQualityFromExtension_WhenNoBitrateIsKnown()
        {
            // Extension-only matching must still land on a rung, otherwise automatic search has
            // nothing to compare a candidate release against.
            var audiobook = CreateAudiobook();
            audiobook.QualityProfile = new QualityProfile
            {
                Name = "Lossless Profile",
                CutoffQuality = "lossless",
                PreferredFormats = ["flac"],
                Qualities = [new() { Quality = "lossless", Priority = 0, Allowed = true }]
            };

            var inspector = CreateInspector(
                directories: [BookPath],
                files: [$"{BookPath}/book.flac"]);

            var presence = inspector.Inspect(audiobook, CreateSettings());

            Assert.True(presence.HasFiles);
            Assert.Equal("lossless", presence.BestQuality);
        }

        [Fact]
        public void Inspect_ReportsNothing_WhenDirectoryCannotBeRead()
        {
            // An unreadable directory must not decide the question either way — search proceeds
            // exactly as it did before this check existed rather than blocking on a permissions bug.
            var fileSystem = new Mock<IFileSystem>();
            fileSystem.Setup(fs => fs.DirectoryExists(BookPath)).Returns(true);
            fileSystem
                .Setup(fs => fs.EnumerateFiles(BookPath, "*", SearchOption.AllDirectories))
                .Throws(new UnauthorizedAccessException("denied"));

            var inspector = new LibraryPresenceInspector(
                fileSystem.Object, CreateNamingService(), NullLogger.Instance);

            var presence = inspector.Inspect(CreateAudiobook(), CreateSettings());

            Assert.False(presence.HasFiles);
        }

        private static LibraryPresenceInspector CreateInspector(string[] directories, string[] files)
        {
            var fileSystem = new Mock<IFileSystem>();
            fileSystem
                .Setup(fs => fs.DirectoryExists(It.IsAny<string>()))
                .Returns((string path) => directories.Contains(path));
            fileSystem
                .Setup(fs => fs.EnumerateFiles(It.IsAny<string>(), "*", SearchOption.AllDirectories))
                .Returns((string path, string _, SearchOption __) =>
                    files.Where(file => file.StartsWith(path + "/", StringComparison.Ordinal)));

            return new LibraryPresenceInspector(fileSystem.Object, CreateNamingService(), NullLogger.Instance);
        }

        private static IFileNamingService CreateNamingService()
        {
            var namingService = new Mock<IFileNamingService>();
            namingService
                .Setup(service => service.ApplyNamingPattern(
                    It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<bool>()))
                .Returns((string pattern, Dictionary<string, object> variables, bool _) =>
                {
                    foreach (var (key, value) in variables)
                    {
                        pattern = pattern.Replace($"{{{key}}}", value?.ToString() ?? string.Empty);
                    }

                    return pattern;
                });

            return namingService.Object;
        }

        private static Audiobook CreateAudiobook() => new()
        {
            Id = 1,
            Title = "A Gentleman in Moscow",
            Authors = ["Amor Towles"],
            QualityProfile = new QualityProfile
            {
                Name = "Standard",
                CutoffQuality = "256kbps",
                PreferredFormats = ["m4b"],
                Qualities = [new() { Quality = "256kbps", Priority = 0, Allowed = true }]
            },
            Editions =
            [
                new()
                {
                    MediaType = EditionMediaType.Audiobook,
                    RootPath = Root
                }
            ]
        };

        private static ApplicationSettings CreateSettings() => new()
        {
            OutputPath = Root,
            FolderNamingPattern = "{Author}/{Title}"
        };
    }
}
