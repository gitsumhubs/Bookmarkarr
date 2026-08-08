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
namespace Bookmarkarr.Tests.Features.Api.Features.Prowlarr
{
    /// <summary>
    /// Guards the generated AudioBook Bay definition against the details that were verified by
    /// hand against a live Prowlarr, each of which fails silently rather than loudly.
    /// </summary>
    public class AudiobookBayDefinitionTests
    {
        [Theory]
        [InlineData(1, 1)]
        [InlineData(3, 3)]
        [InlineData(10, 10)]
        public void Build_EmitsOnePathPerRequestedPage(int pages, int expectedPaths)
        {
            var yaml = AudiobookBayDefinition.Build(pages);

            var pathCount = yaml.Split('\n').Count(line => line.TrimStart().StartsWith("- path:", StringComparison.Ordinal));

            Assert.Equal(expectedPaths, pathCount);
        }

        [Fact]
        public void Build_FirstPathIsSiteRoot_AndSubsequentPathsArePaged()
        {
            var yaml = AudiobookBayDefinition.Build(3);

            Assert.Contains("- path: /", yaml, StringComparison.Ordinal);
            Assert.Contains("- path: \"page/2/\"", yaml, StringComparison.Ordinal);
            Assert.Contains("- path: \"page/3/\"", yaml, StringComparison.Ordinal);
            Assert.DoesNotContain("- path: \"page/4/\"", yaml, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        [InlineData(99)]
        public void Build_ClampsPagesIntoSupportedRange(int requested)
        {
            var yaml = AudiobookBayDefinition.Build(requested);

            var pathCount = yaml.Split('\n').Count(line => line.TrimStart().StartsWith("- path:", StringComparison.Ordinal));

            Assert.InRange(pathCount, AudiobookBayDefinition.MinimumPages, AudiobookBayDefinition.MaximumPages);
        }

        /// <summary>
        /// The site's own category field carries genre tags, so without an explicit audiobook
        /// category Bookmarkarr's content-type filter discards every result and the patch looks
        /// like it returned nothing.
        /// </summary>
        [Fact]
        public void Build_StatesTheAudiobookCategoryExplicitly()
        {
            var yaml = AudiobookBayDefinition.Build(3);

            Assert.Contains("- {id: \"Audiobook\", cat: Audio/Audiobook, desc: \"Audiobook\"}", yaml, StringComparison.Ordinal);
            Assert.Contains("category:\n      text: \"Audiobook\"", yaml.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        }

        /// <summary>
        /// Cardigann's own template braces must survive C# interpolation untouched, or Prowlarr
        /// searches for a literal "{{ .Keywords }}".
        /// </summary>
        [Fact]
        public void Build_PreservesCardigannTemplatePlaceholders()
        {
            var yaml = AudiobookBayDefinition.Build(3);

            Assert.Contains("s: \"{{ .Keywords }}\"", yaml, StringComparison.Ordinal);
            Assert.Contains("text: \"{{ .Result.booktitle }} [{{ .Result.fileformat }}]\"", yaml, StringComparison.Ordinal);
        }

        /// <summary>
        /// Regex braces are the other casualty of getting interpolation escaping wrong.
        /// </summary>
        /// <remarks>
        /// The backslashes are doubled on disk on purpose: these sit inside YAML double-quoted
        /// scalars, which unescape <c>\\s</c> to the <c>\s</c> Cardigann ultimately compiles.
        /// Emitting a single backslash here would leave Prowlarr with a broken pattern.
        /// </remarks>
        [Fact]
        public void Build_PreservesRegexQuantifiers()
        {
            var yaml = AudiobookBayDefinition.Build(3);

            Assert.Contains("args: ([A-Fa-f0-9]{40})", yaml, StringComparison.Ordinal);
            Assert.Contains(@"args: ""Posted:\\s*(\\d{1,2}\\s+\\w{3}\\s+\\d{4})""", yaml, StringComparison.Ordinal);
            Assert.Contains(@"args: ""File Size:\\s*([\\d.]+\\s*[KMGT]B)""", yaml, StringComparison.Ordinal);
        }

        [Fact]
        public void Build_ResolvesMagnetsFromTheDetailPageInfoHash()
        {
            var yaml = AudiobookBayDefinition.Build(3);

            Assert.Contains("selector: td:contains(\"Info Hash:\") ~ td", yaml, StringComparison.Ordinal);
        }

        /// <summary>
        /// The site refuses clients identifying as Prowlarr; forks work around that process-wide.
        /// Bookmarkarr deliberately ships no agent override of its own.
        /// </summary>
        [Fact]
        public void Build_DoesNotOverrideTheUserAgent()
        {
            var yaml = AudiobookBayDefinition.Build(3);

            Assert.DoesNotContain("User-Agent", yaml, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ProjectedResults_ScalesWithPagesAndClamps()
        {
            Assert.Equal(27, AudiobookBayDefinition.ProjectedResults(3));
            Assert.Equal(9, AudiobookBayDefinition.ProjectedResults(1));
            Assert.Equal(
                AudiobookBayDefinition.MaximumPages * AudiobookBayDefinition.ResultsPerPage,
                AudiobookBayDefinition.ProjectedResults(500));
        }
    }
}
