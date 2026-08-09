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
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Bookmarkarr.Application.Configuration.Contracts.Repositories;

namespace Bookmarkarr.Api.Features.Prowlarr
{
    /// <summary>
    /// Detects Prowlarr's single-page AudioBook Bay indexer and replaces it with a paginated
    /// Cardigann definition.
    /// </summary>
    /// <remarks>
    /// Prowlarr exposes no API for installing an indexer definition, so the file has to be written
    /// to its definitions directory, which requires that directory to be mounted into Bookmarkarr.
    /// That mount is the one part of this that cannot be arranged from inside the application, so
    /// its absence is reported as a first-class outcome rather than an error — the caller falls
    /// back to showing manual instructions.
    ///
    /// Prowlarr reloads definitions from disk without a restart, which is what makes the rest of
    /// the sequence possible: write the file, wait for Prowlarr to notice, create the indexer over
    /// the API, then repoint Bookmarkarr's own indexer record at it.
    /// </remarks>
    public sealed partial class AudiobookBayPatchWorkflow
    {
        /// <summary>
        /// Mount points checked for Prowlarr's configuration directory, most conventional first.
        /// </summary>
        private static readonly string[] CandidateConfigDirectories =
        [
            "/prowlarr-config",
            "/prowlarr",
            "/config/prowlarr"
        ];

        /// <summary>
        /// Every precondition, in the order they depend on one another, so a caller that stops
        /// early still reports the rest rather than omitting them.
        /// </summary>
        private static readonly (string Id, string Label)[] CheckSequence =
        [
            ("prowlarrConfigured", "Prowlarr connection saved"),
            ("prowlarrReachable", "Prowlarr reachable"),
            ("audiobookBayIndexer", "AudioBook Bay indexer in use"),
            ("definitionsDirectory", "Prowlarr definitions directory"),
            ("definitionsWritable", "Definitions directory writable"),
            ("definitionInstalled", "Paginated definition installed"),
            ("siteReachable", "AudioBook Bay answering searches")
        ];

        private static readonly TimeSpan DefinitionReloadTimeout = TimeSpan.FromSeconds(45);
        private static readonly TimeSpan DefinitionPollInterval = TimeSpan.FromSeconds(3);

        private readonly IIndexerRepository _indexerRepository;
        private readonly IConfigurationService _configurationService;
        private readonly IApplicationSettingsRepository _settingsRepository;
        private readonly IFileSystem _fileSystem;
        private readonly HttpClient _httpClient;
        private readonly ILogger<AudiobookBayPatchWorkflow> _logger;

        public AudiobookBayPatchWorkflow(
            IIndexerRepository indexerRepository,
            IConfigurationService configurationService,
            IApplicationSettingsRepository settingsRepository,
            IFileSystem fileSystem,
            HttpClient httpClient,
            ILogger<AudiobookBayPatchWorkflow> logger)
        {
            _indexerRepository = indexerRepository;
            _configurationService = configurationService;
            _settingsRepository = settingsRepository;
            _fileSystem = fileSystem;
            _httpClient = httpClient;
            _logger = logger;
        }

        /// <summary>
        /// Reports whether a Bookmarkarr indexer is backed by Prowlarr's single-page AudioBook Bay
        /// implementation, and whether this instance could apply the patch itself.
        /// </summary>
        /// <remarks>
        /// Kept as the narrow question the startup prompt asks: should the patch be offered right
        /// now? <see cref="GetDiagnosticsAsync"/> answers the wider one for the settings tab.
        /// </remarks>
        public async Task<AudiobookBayPatchStatus> GetStatusAsync(CancellationToken ct = default)
        {
            var diagnostics = await GetDiagnosticsAsync(ct);
            if (diagnostics.PatchState != AudiobookBayPatchState.NotPatched)
            {
                return AudiobookBayPatchStatus.NotApplicable(DescribeState(diagnostics.PatchState, diagnostics.Checks));
            }

            return AudiobookBayPatchStatus.Available(
                diagnostics.IndexerId!.Value,
                diagnostics.IndexerName!,
                diagnostics.ProwlarrIndexerId!.Value,
                diagnostics.CurrentResultCap,
                diagnostics.ProjectedResults,
                diagnostics.CanAutoApply,
                diagnostics.DefinitionsDirectory);
        }

        /// <summary>
        /// Describe a definitions directory that could not be resolved, separating the mount that
        /// was never made from the one that was made and points somewhere else.
        /// </summary>
        /// <remarks>
        /// The Compose file ships this mount enabled with a placeholder host path, so an operator
        /// who has not pointed it at Prowlarr yet has an existing but empty <c>/prowlarr-config</c>.
        /// Reporting that as "not mounted" would send them to fix something already correct.
        /// </remarks>
        private AudiobookBayCheck BuildMissingDirectoryCheck(string? savedDirectory)
        {
            if (savedDirectory != null)
            {
                return new AudiobookBayCheck(
                    "definitionsDirectory",
                    "Prowlarr definitions directory",
                    AudiobookBayCheckState.Fail,
                    $"'{savedDirectory}' is not readable from Bookmarkarr, or does not look like Prowlarr's config directory.",
                    "Set a different path below, or install the definition by hand.");
            }

            var mountedButUnrecognised = CandidateConfigDirectories.FirstOrDefault(DirectoryExistsSafely);
            if (mountedButUnrecognised != null)
            {
                return new AudiobookBayCheck(
                    "definitionsDirectory",
                    "Prowlarr definitions directory",
                    AudiobookBayCheckState.Fail,
                    $"'{mountedButUnrecognised}' is mounted but carries neither Prowlarr's config.xml nor a Definitions folder, so it is not Prowlarr's config directory.",
                    "Point the mount at Prowlarr's own config directory — set BOOKMARKARR_PROWLARR_CONFIG_PATH in Bookmarkarr's .env, then `docker compose up -d` — or set the path below.");
            }

            return new AudiobookBayCheck(
                "definitionsDirectory",
                "Prowlarr definitions directory",
                AudiobookBayCheckState.Fail,
                "Prowlarr's config directory is not mounted into Bookmarkarr.",
                "Mount it — add `- /path/to/prowlarr/config:/prowlarr-config` to Bookmarkarr's compose and recreate the container — or set the path below. Prowlarr on another host cannot be mounted; install the definition by hand instead.");
        }

        private static bool CanRevert(ApplicationSettings? settings) =>
            settings?.AudiobookBayPatchIndexerId != null
            && !string.IsNullOrWhiteSpace(settings.AudiobookBayPatchPreviousIndexerUrl);

        /// <summary>
        /// Finds the enabled Bookmarkarr indexer that searches one of <paramref name="prowlarrIds"/>.
        /// </summary>
        private static (int IndexerId, string Name, int ProwlarrIndexerId)? FindEnabledMatch(
            IEnumerable<Indexer> indexers,
            string baseUrl,
            IEnumerable<int> prowlarrIds)
        {
            var enabled = indexers.Where(i => i.IsEnabled).ToList();

            foreach (var prowlarrId in prowlarrIds)
            {
                var expectedUrl = ProwlarrImportUrlPlanner.NormalizeProxyUrl(
                    ProwlarrImportUrlPlanner.BuildProxyUrl(baseUrl, prowlarrId));

                var match = enabled.FirstOrDefault(i =>
                    string.Equals(
                        ProwlarrImportUrlPlanner.NormalizeProxyUrl(i.Url),
                        expectedUrl,
                        StringComparison.OrdinalIgnoreCase));

                if (match != null)
                {
                    return (match.Id, match.Name, prowlarrId);
                }
            }

            return null;
        }

        /// <summary>Diagnostics for the cases where Prowlarr could not be consulted at all.</summary>
        private AudiobookBayDiagnostics Undetermined(
            List<AudiobookBayCheck> checks,
            ApplicationSettings? settings,
            string? savedDirectory,
            AudiobookBayPatchState state)
        {
            var resolved = ResolveDefinitionsDirectory(null, savedDirectory);

            return new AudiobookBayDiagnostics(
                state,
                Ordered(checks),
                null,
                null,
                null,
                resolved.Directory,
                resolved.FromOverride,
                resolved.Directory != null,
                CanRevert(settings),
                resolved.Directory != null
                    && FileExistsSafely(AudiobookBayDefinition.DefinitionPathIn(resolved.Directory)),
                settings?.AudiobookBayPatchPages,
                0,
                AudiobookBayDefinition.ProjectedResults(AudiobookBayDefinition.DefaultPages));
        }

        /// <summary>
        /// Runs every precondition the patch depends on and reports each one separately.
        /// </summary>
        /// <remarks>
        /// One boolean cannot distinguish "already patched" from "cannot tell", and those call for
        /// opposite advice. Each check therefore carries its own outcome and its own remedy, and a
        /// check that could not run because an earlier one failed reports Unknown rather than
        /// borrowing a pass or a failure it did not earn.
        ///
        /// Site reachability is deliberately not checked here: it costs a live search against
        /// AudioBook Bay, so it is left to <see cref="ProbeSearchAsync"/> on request.
        /// </remarks>
        public async Task<AudiobookBayDiagnostics> GetDiagnosticsAsync(CancellationToken ct = default)
        {
            var checks = new List<AudiobookBayCheck>();
            var settings = await _settingsRepository.GetAsync(ct);
            var savedDirectory = Trimmed(settings?.AudiobookBayDefinitionsDirectory);

            var connection = await _configurationService.GetProwlarrImportSettingsAsync(includeSecret: true);
            if (string.IsNullOrWhiteSpace(connection.Url) || string.IsNullOrWhiteSpace(connection.ApiKey))
            {
                checks.Add(new AudiobookBayCheck(
                    "prowlarrConfigured",
                    "Prowlarr connection saved",
                    AudiobookBayCheckState.Fail,
                    "No Prowlarr URL or API key is saved in Bookmarkarr.",
                    "Enter Prowlarr's URL and API key below, then Test and save. Nothing is imported."));

                return Undetermined(checks, settings, savedDirectory, AudiobookBayPatchState.Unknown);
            }

            var baseUrl = ProwlarrImportUrlPlanner.BuildBaseUrl(connection.Url, connection.Port);
            if (!OutboundRequestSecurity.TryValidateExternalHttpUrl(baseUrl, out var blockedReason, allowPrivateTargets: true))
            {
                checks.Add(new AudiobookBayCheck(
                    "prowlarrConfigured",
                    "Prowlarr connection saved",
                    AudiobookBayCheckState.Fail,
                    $"The saved Prowlarr address was rejected: {blockedReason}",
                    "Correct the Prowlarr URL below, then Test and save."));

                return Undetermined(checks, settings, savedDirectory, AudiobookBayPatchState.Unknown);
            }

            checks.Add(new AudiobookBayCheck(
                "prowlarrConfigured",
                "Prowlarr connection saved",
                AudiobookBayCheckState.Pass,
                LogRedaction.SanitizeUrl(baseUrl),
                null));

            JsonDocument? prowlarrIndexers;
            try
            {
                prowlarrIndexers = await GetJsonAsync($"{baseUrl}/api/v1/indexer", connection.ApiKey!, ct);
            }
            catch (Exception ex) when (IsTransportFailure(ex))
            {
                _logger.LogWarning(ex, "Could not reach Prowlarr at {Url} while checking AudioBook Bay", LogRedaction.SanitizeUrl(baseUrl));
                checks.Add(new AudiobookBayCheck(
                    "prowlarrReachable",
                    "Prowlarr reachable",
                    AudiobookBayCheckState.Fail,
                    $"No response from {LogRedaction.SanitizeUrl(baseUrl)}.",
                    "Check Prowlarr is running and that Bookmarkarr's container can reach it — a container name only resolves when both share a Docker network."));

                return Undetermined(checks, settings, savedDirectory, AudiobookBayPatchState.Unknown);
            }

            if (prowlarrIndexers == null || prowlarrIndexers.RootElement.ValueKind != JsonValueKind.Array)
            {
                prowlarrIndexers?.Dispose();
                checks.Add(new AudiobookBayCheck(
                    "prowlarrReachable",
                    "Prowlarr reachable",
                    AudiobookBayCheckState.Fail,
                    "Prowlarr answered, but not with a list of indexers.",
                    "Confirm the saved API key is Prowlarr's, and that the URL points at Prowlarr rather than a reverse proxy error page."));

                return Undetermined(checks, settings, savedDirectory, AudiobookBayPatchState.Unknown);
            }

            using (prowlarrIndexers)
            {
                checks.Add(new AudiobookBayCheck(
                    "prowlarrReachable",
                    "Prowlarr reachable",
                    AudiobookBayCheckState.Pass,
                    null,
                    null));

                var compiledIds = new List<int>();
                var patchedIds = new List<int>();

                foreach (var element in prowlarrIndexers.RootElement.EnumerateArray())
                {
                    if (!element.TryGetProperty("id", out var idProp) || idProp.ValueKind != JsonValueKind.Number)
                    {
                        continue;
                    }

                    var implementation = ReadString(element, "implementation");
                    if (string.Equals(implementation, AudiobookBayDefinition.CompiledImplementation, StringComparison.OrdinalIgnoreCase))
                    {
                        compiledIds.Add(idProp.GetInt32());
                        continue;
                    }

                    // The display name belongs to the operator; the definition name follows the
                    // file Bookmarkarr wrote, so it is what identifies our indexer later.
                    if (string.Equals(implementation, AudiobookBayDefinition.CardigannImplementation, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(ReadString(element, "definitionName"), AudiobookBayDefinition.DefinitionFileStem, StringComparison.OrdinalIgnoreCase))
                    {
                        patchedIds.Add(idProp.GetInt32());
                    }
                }

                var bookmarkarrIndexers = await _indexerRepository.GetAllAsync(ct);
                var patchedMatch = FindEnabledMatch(bookmarkarrIndexers, baseUrl, patchedIds);
                var compiledMatch = FindEnabledMatch(bookmarkarrIndexers, baseUrl, compiledIds);

                var state = patchedMatch != null
                    ? AudiobookBayPatchState.Patched
                    : compiledMatch != null
                        ? AudiobookBayPatchState.NotPatched
                        : AudiobookBayPatchState.NotApplicable;

                var match = patchedMatch ?? compiledMatch;

                checks.Add(state switch
                {
                    AudiobookBayPatchState.Patched => new AudiobookBayCheck(
                        "audiobookBayIndexer",
                        "AudioBook Bay indexer in use",
                        AudiobookBayCheckState.Pass,
                        $"'{match!.Value.Name}' searches the paginated definition.",
                        null),
                    AudiobookBayPatchState.NotPatched => new AudiobookBayCheck(
                        "audiobookBayIndexer",
                        "AudioBook Bay indexer in use",
                        AudiobookBayCheckState.Warn,
                        $"'{match!.Value.Name}' searches Prowlarr's compiled indexer, capped at {AudiobookBayDefinition.ResultsPerPage} results.",
                        "Apply the patch below."),
                    _ => new AudiobookBayCheck(
                        "audiobookBayIndexer",
                        "AudioBook Bay indexer in use",
                        AudiobookBayCheckState.Warn,
                        compiledIds.Count == 0 && patchedIds.Count == 0
                            ? "Prowlarr has no AudioBook Bay indexer."
                            : "No enabled Bookmarkarr indexer points at Prowlarr's AudioBook Bay.",
                        "Add AudioBook Bay in Prowlarr, then import it in Settings > Indexers.")
                });

                var resolved = ResolveDefinitionsDirectory(null, savedDirectory);
                checks.Add(resolved.Directory != null
                    ? new AudiobookBayCheck(
                        "definitionsDirectory",
                        "Prowlarr definitions directory",
                        AudiobookBayCheckState.Pass,
                        resolved.FromOverride
                            ? $"{resolved.Directory} (saved path)"
                            : resolved.Directory,
                        null)
                    : BuildMissingDirectoryCheck(savedDirectory));

                var definitionPath = resolved.Directory == null
                    ? null
                    : AudiobookBayDefinition.DefinitionPathIn(resolved.Directory);

                var installed = definitionPath != null && FileExistsSafely(definitionPath);

                if (resolved.Directory == null)
                {
                    checks.Add(Unknown("definitionsWritable", "Definitions directory writable", "Nothing to write to yet."));
                    checks.Add(Unknown("definitionInstalled", "Paginated definition installed", "Cannot be read from here."));
                }
                else
                {
                    var write = TestWrite(resolved.Directory);
                    checks.Add(write.Writable
                        ? new AudiobookBayCheck(
                            "definitionsWritable",
                            "Definitions directory writable",
                            AudiobookBayCheckState.Pass,
                            null,
                            null)
                        : new AudiobookBayCheck(
                            "definitionsWritable",
                            "Definitions directory writable",
                            AudiobookBayCheckState.Fail,
                            write.Message,
                            "Mount the directory read-write and make sure Bookmarkarr's PUID/PGID may write to it. Otherwise install the definition by hand below."));

                    checks.Add(installed
                        ? new AudiobookBayCheck(
                            "definitionInstalled",
                            "Paginated definition installed",
                            AudiobookBayCheckState.Pass,
                            definitionPath,
                            null)
                        : new AudiobookBayCheck(
                            "definitionInstalled",
                            "Paginated definition installed",
                            AudiobookBayCheckState.Warn,
                            $"No file at {definitionPath}.",
                            null));
                }

                // Only a real search separates a working fork from a stock Prowlarr that AudioBook
                // Bay answers with a 404, and that is too slow to run on every page load.
                checks.Add(Unknown(
                    "siteReachable",
                    "AudioBook Bay answering searches",
                    "Run the search test to check. AudioBook Bay serves a 404 to clients identifying as Prowlarr, so a stock Prowlarr installs this definition and then returns nothing."));

                var installedPages = state == AudiobookBayPatchState.Patched ? settings?.AudiobookBayPatchPages : null;

                return new AudiobookBayDiagnostics(
                    state,
                    Ordered(checks),
                    match?.IndexerId,
                    match?.Name,
                    match?.ProwlarrIndexerId,
                    resolved.Directory,
                    resolved.FromOverride,
                    resolved.Directory != null,
                    CanRevert(settings),
                    installed,
                    installedPages,
                    state == AudiobookBayPatchState.Patched
                        ? AudiobookBayDefinition.ProjectedResults(installedPages ?? AudiobookBayDefinition.DefaultPages)
                        : AudiobookBayDefinition.ResultsPerPage,
                    AudiobookBayDefinition.ProjectedResults(installedPages ?? AudiobookBayDefinition.DefaultPages));
            }
        }
    }
}
