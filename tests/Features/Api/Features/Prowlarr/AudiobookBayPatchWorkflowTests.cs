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
using Bookmarkarr.Application.Configuration.Contracts.Repositories;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bookmarkarr.Tests.Features.Api.Features.Prowlarr
{
    /// <summary>
    /// Covers the answers the settings tab depends on, all of which are reachable without a live
    /// Prowlarr: what the diagnostics report when it cannot be consulted, and that a revert refuses
    /// rather than guesses when nothing was recorded.
    /// </summary>
    public class AudiobookBayPatchWorkflowTests
    {
        [Fact]
        public void DefinitionFileStem_MatchesTheFileActuallyWritten()
        {
            // Prowlarr reports a Cardigann indexer's origin as the file name without its extension,
            // and that string is how the patched indexer is recognised. If the two drift apart, a
            // patched instance silently reports itself unpatched.
            Assert.Equal(
                Path.GetFileNameWithoutExtension(AudiobookBayDefinition.FileName),
                AudiobookBayDefinition.DefinitionFileStem);
        }

        [Fact]
        public void DefinitionPath_LandsWhereProwlarrActuallyReads()
        {
            // Prowlarr reads <config>/Definitions/Custom. Every path in this feature was composed as
            // <config>/Custom, so applying wrote the file somewhere Prowlarr never looks, waited out
            // the reload, and failed — while the installed check looked in the same wrong place and
            // always answered no. Only the manual instructions were right, because the UI prepends
            // "Definitions/" itself.
            var path = AudiobookBayDefinition.DefinitionPathIn("/prowlarr-config");

            Assert.Equal(
                Path.Combine("/prowlarr-config", "Definitions", "Custom", AudiobookBayDefinition.FileName),
                path);
            Assert.Equal(
                Path.Combine("/prowlarr-config", "Definitions", "Custom"),
                AudiobookBayDefinition.CustomDirectoryIn("/prowlarr-config"));
        }

        [Fact]
        public async Task GetDiagnostics_WithAnEmptyProwlarrMount_SaysMountedNotMissing()
        {
            // The Compose file ships /prowlarr-config mounted at an empty default, so the common
            // first-run state is "mounted, but not Prowlarr". Calling that "not mounted" would send
            // the operator to add a volume they already have.
            var workflow = BuildWorkflow(
                out _,
                out _,
                fileSystem: files => files
                    .Setup(f => f.DirectoryExists("/prowlarr-config"))
                    .Returns(true),
                connection: new ProwlarrImportConnectionSettings
                {
                    Url = "http://prowlarr",
                    Port = 9696,
                    ApiKey = "key"
                },
                handler: new StubHandler("[]"));

            var diagnostics = await workflow.GetDiagnosticsAsync();

            var directory = Assert.Single(diagnostics.Checks, c => c.Id == "definitionsDirectory");
            Assert.Equal(AudiobookBayCheckState.Fail, directory.State);
            Assert.Contains("/prowlarr-config", directory.Detail);
            Assert.Contains("mounted", directory.Detail);
            Assert.DoesNotContain("is not mounted", directory.Detail);
            Assert.Contains("BOOKMARKARR_PROWLARR_CONFIG_PATH", directory.Remedy);
        }

        [Fact]
        public async Task GetDiagnostics_WithNoProwlarrMountAtAll_StillSaysNotMounted()
        {
            var workflow = BuildWorkflow(
                out _,
                out _,
                connection: new ProwlarrImportConnectionSettings
                {
                    Url = "http://prowlarr",
                    Port = 9696,
                    ApiKey = "key"
                },
                handler: new StubHandler("[]"));

            var diagnostics = await workflow.GetDiagnosticsAsync();

            var directory = Assert.Single(diagnostics.Checks, c => c.Id == "definitionsDirectory");
            Assert.Equal(AudiobookBayCheckState.Fail, directory.State);
            Assert.Contains("is not mounted", directory.Detail);
        }

        [Fact]
        public async Task GetDiagnostics_WithoutProwlarrConfigured_ReportsUnknownRatherThanUnpatched()
        {
            var workflow = BuildWorkflow(out _, out _);

            var diagnostics = await workflow.GetDiagnosticsAsync();

            // "Cannot tell" must never be rendered as "not patched": the advice differs entirely.
            Assert.Equal(AudiobookBayPatchState.Unknown, diagnostics.PatchState);

            var configured = Assert.Single(diagnostics.Checks, c => c.Id == "prowlarrConfigured");
            Assert.Equal(AudiobookBayCheckState.Fail, configured.State);
            Assert.False(string.IsNullOrWhiteSpace(configured.Remedy));
        }

        [Fact]
        public async Task GetDiagnostics_AlwaysReportsEveryCheck_EvenWhenItStopsEarly()
        {
            var workflow = BuildWorkflow(out _, out _);

            var diagnostics = await workflow.GetDiagnosticsAsync();

            var expected = new[]
            {
                "prowlarrConfigured",
                "prowlarrReachable",
                "audiobookBayIndexer",
                "definitionsDirectory",
                "definitionsWritable",
                "definitionInstalled",
                "siteReachable"
            };

            Assert.Equal(expected, diagnostics.Checks.Select(c => c.Id).ToArray());

            // A check that never ran must not borrow a verdict it did not earn.
            Assert.All(
                diagnostics.Checks.Where(c => c.Id != "prowlarrConfigured"),
                check => Assert.Equal(AudiobookBayCheckState.Unknown, check.State));
        }

        [Fact]
        public async Task GetStatus_WithoutProwlarrConfigured_DoesNotOfferThePatch()
        {
            var workflow = BuildWorkflow(out _, out _);

            var status = await workflow.GetStatusAsync();

            Assert.False(status.PatchAvailable);
            Assert.False(string.IsNullOrWhiteSpace(status.Reason));
        }

        [Fact]
        public async Task Revert_WithNothingRecorded_RefusesInsteadOfGuessing()
        {
            var workflow = BuildWorkflow(out _, out var indexers);

            var result = await workflow.RevertAsync();

            Assert.Equal(AudiobookBayRevertResultKind.NothingRecorded, result.Kind);
            Assert.False(string.IsNullOrWhiteSpace(result.Message));

            // Nothing may be repointed on a guess: the wrong choice silently stops ABB searches.
            indexers.Verify(r => r.UpdateAsync(It.IsAny<Indexer>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Revert_RestoresTheRecordedUrl_AndClearsTheRecord()
        {
            var settings = new ApplicationSettings
            {
                Id = 1,
                AudiobookBayPatchIndexerId = 7,
                AudiobookBayPatchPreviousIndexerUrl = "http://prowlarr:9696/2/api",
                AudiobookBayPatchProwlarrIndexerId = 9,
                AudiobookBayPatchPages = 3,
                AudiobookBayDefinitionsDirectory = "/prowlarr-config"
            };

            var workflow = BuildWorkflow(out var settingsRepository, out var indexers, settings);

            var indexer = new Indexer
            {
                Id = 7,
                Name = "AudioBook Bay (Prowlarr)",
                Url = "http://prowlarr:9696/9/api",
                IsEnabled = true
            };
            indexers.Setup(r => r.GetByIdAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(indexer);

            var result = await workflow.RevertAsync();

            Assert.Equal(AudiobookBayRevertResultKind.Success, result.Kind);
            Assert.Equal("http://prowlarr:9696/2/api", indexer.Url);

            Assert.Null(settings.AudiobookBayPatchIndexerId);
            Assert.Null(settings.AudiobookBayPatchPreviousIndexerUrl);
            Assert.Null(settings.AudiobookBayPatchProwlarrIndexerId);
            Assert.Null(settings.AudiobookBayPatchPages);

            // Where Prowlarr lives is not part of the patch, so it survives the revert.
            Assert.Equal("/prowlarr-config", settings.AudiobookBayDefinitionsDirectory);

            settingsRepository.Verify(
                r => r.SaveAsync(It.IsAny<ApplicationSettings>(), It.IsAny<CancellationToken>()),
                Times.AtLeastOnce);
        }

        [Fact]
        public async Task ProbeDirectory_RejectsADirectoryWithoutProwlarrsFingerprint()
        {
            var workflow = BuildWorkflow(out _, out _, fileSystem: fs =>
            {
                fs.Setup(f => f.DirectoryExists("/somewhere")).Returns(true);
                fs.Setup(f => f.FileExists(Path.Combine("/somewhere", "config.xml"))).Returns(false);
                fs.Setup(f => f.DirectoryExists(Path.Combine("/somewhere", "Definitions"))).Returns(false);
            });

            var probe = await workflow.ProbeDirectoryAsync("/somewhere");

            Assert.True(probe.Exists);
            Assert.False(probe.LooksLikeProwlarr);
            Assert.False(probe.Writable);
        }

        [Fact]
        public async Task ProbeDirectory_ReportsAReadOnlyMountAsUnwritable()
        {
            // The failure this exists to catch: a read-only bind mount passes every inspection and
            // only fails when the definition is written, where it looks like a reload timeout.
            var workflow = BuildWorkflow(out _, out _, fileSystem: fs =>
            {
                fs.Setup(f => f.DirectoryExists("/prowlarr-config")).Returns(true);
                fs.Setup(f => f.FileExists(Path.Combine("/prowlarr-config", "config.xml"))).Returns(true);
                fs.Setup(f => f.CreateDirectory(It.IsAny<string>()));
                fs.Setup(f => f.WriteAllText(It.IsAny<string>(), It.IsAny<string>()))
                    .Throws(new UnauthorizedAccessException("Read-only file system"));
            });

            var probe = await workflow.ProbeDirectoryAsync("/prowlarr-config");

            Assert.True(probe.LooksLikeProwlarr);
            Assert.False(probe.Writable);
            Assert.Contains("Read-only file system", probe.Message);
        }

        /// <summary>Answers every Prowlarr request with one canned payload.</summary>
        private sealed class StubHandler(string payload) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
                });
        }

        private static AudiobookBayPatchWorkflow BuildWorkflow(
            out Mock<IApplicationSettingsRepository> settingsRepository,
            out Mock<IIndexerRepository> indexerRepository,
            ApplicationSettings? settings = null,
            Action<Mock<IFileSystem>>? fileSystem = null,
            ProwlarrImportConnectionSettings? connection = null,
            HttpMessageHandler? handler = null)
        {
            settingsRepository = new Mock<IApplicationSettingsRepository>();
            settingsRepository
                .Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(settings);
            settingsRepository
                .Setup(r => r.SaveAsync(It.IsAny<ApplicationSettings>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ApplicationSettings s, CancellationToken _) => s);

            indexerRepository = new Mock<IIndexerRepository>();
            indexerRepository
                .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

            var configuration = new Mock<IConfigurationService>();
            configuration
                .Setup(c => c.GetProwlarrImportSettingsAsync(It.IsAny<bool>()))
                .ReturnsAsync(connection ?? new ProwlarrImportConnectionSettings());

            var files = new Mock<IFileSystem>();
            fileSystem?.Invoke(files);

            return new AudiobookBayPatchWorkflow(
                indexerRepository.Object,
                configuration.Object,
                settingsRepository.Object,
                files.Object,
                handler == null ? new HttpClient() : new HttpClient(handler),
                NullLogger<AudiobookBayPatchWorkflow>.Instance);
        }
    }
}
