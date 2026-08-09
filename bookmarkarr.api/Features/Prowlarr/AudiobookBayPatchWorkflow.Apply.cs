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
    /// Applying and undoing the patch, and the probes that answer whether either would
    /// work before it is attempted.
    /// </summary>
    public sealed partial class AudiobookBayPatchWorkflow
    {
        /// <summary>
        /// Writes the paginated definition, waits for Prowlarr to load it, creates the indexer, and
        /// repoints Bookmarkarr's record at it.
        /// </summary>
        /// <param name="pages">Pages of results the definition should read. Nine results per page.</param>
        /// <param name="definitionsDirectoryOverride">
        /// Prowlarr's config directory as mounted here. Null falls back to the saved path, then to
        /// the conventional mount points.
        /// </param>
        /// <param name="definitionAlreadyInstalled">
        /// Set when the operator copied the definition into Prowlarr themselves. Everything after
        /// the file write — waiting for the reload, creating the indexer, repointing Bookmarkarr —
        /// still runs over the API, so an unmountable Prowlarr costs one manual step rather than
        /// the whole procedure.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        public async Task<AudiobookBayPatchResult> ApplyAsync(
            int pages,
            string? definitionsDirectoryOverride,
            bool definitionAlreadyInstalled = false,
            CancellationToken ct = default)
        {
            var status = await GetStatusAsync(ct);
            if (!status.PatchAvailable)
            {
                return AudiobookBayPatchResult.Failed(status.Reason ?? "AudioBook Bay patch does not apply here");
            }

            var connection = await _configurationService.GetProwlarrImportSettingsAsync(includeSecret: true);
            var baseUrl = ProwlarrImportUrlPlanner.BuildBaseUrl(connection.Url, connection.Port);
            var apiKey = connection.ApiKey!;

            string? definitionPath = null;

            if (definitionAlreadyInstalled)
            {
                _logger.LogInformation("Wiring up an AudioBook Bay definition that was installed by hand");
            }
            else
            {
                var settings = await _settingsRepository.GetAsync(ct);
                var resolved = ResolveDefinitionsDirectory(definitionsDirectoryOverride, Trimmed(settings?.AudiobookBayDefinitionsDirectory));
                if (resolved.Directory == null)
                {
                    return AudiobookBayPatchResult.MountRequired(
                        string.IsNullOrWhiteSpace(definitionsDirectoryOverride)
                            ? "Prowlarr's config directory is not mounted into Bookmarkarr"
                            : $"'{definitionsDirectoryOverride}' does not look like a Prowlarr config directory");
                }

                var customDirectory = Path.Combine(resolved.Directory, AudiobookBayDefinition.CustomSubdirectory);
                definitionPath = Path.Combine(customDirectory, AudiobookBayDefinition.FileName);

                try
                {
                    _fileSystem.CreateDirectory(customDirectory);
                    _fileSystem.WriteAllText(definitionPath, AudiobookBayDefinition.Build(pages));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
                {
                    _logger.LogWarning(ex, "Could not write the AudioBook Bay definition to {Path}", definitionPath);
                    return AudiobookBayPatchResult.MountRequired($"Could not write to {customDirectory}: {ex.Message}");
                }

                _logger.LogInformation("Wrote AudioBook Bay definition for {Pages} page(s) to {Path}", pages, definitionPath);
            }

            // Prowlarr rescans the definitions directory on its own; poll rather than restart it.
            var loaded = await WaitForDefinitionAsync(baseUrl, apiKey, ct);
            if (!loaded)
            {
                return AudiobookBayPatchResult.Failed(
                    definitionAlreadyInstalled
                        ? $"Prowlarr did not list the definition within {DefinitionReloadTimeout.TotalSeconds:0} seconds. " +
                          $"Check the file is at Definitions/{AudiobookBayDefinition.CustomSubdirectory}/{AudiobookBayDefinition.FileName} inside Prowlarr's config directory and readable by Prowlarr."
                        : $"Prowlarr did not pick up the definition within {DefinitionReloadTimeout.TotalSeconds:0} seconds. " +
                          $"The file was written to {definitionPath} — check Prowlarr can read it.");
            }

            int newIndexerId;
            try
            {
                newIndexerId = await CreateProwlarrIndexerAsync(baseUrl, apiKey, ct);
            }
            catch (Exception ex) when (IsTransportFailure(ex) || ex is JsonException or InvalidOperationException)
            {
                _logger.LogWarning(ex, "Could not create the patched AudioBook Bay indexer in Prowlarr");
                return AudiobookBayPatchResult.Failed($"Prowlarr loaded the definition but rejected the new indexer: {ex.Message}");
            }

            var indexer = await _indexerRepository.GetByIdAsync(status.IndexerId!.Value, ct);
            if (indexer == null)
            {
                return AudiobookBayPatchResult.Failed("The Bookmarkarr indexer disappeared while patching");
            }

            var previousUrl = indexer.Url;
            indexer.Url = ProwlarrImportUrlPlanner.NormalizeProxyUrl(
                ProwlarrImportUrlPlanner.BuildProxyUrl(baseUrl, newIndexerId));

            // Applying the patch is the moment request volume multiplies by the page count, so it
            // is the moment to give the indexer a budget. An operator who has already chosen one
            // keeps it — this fills an empty setting rather than overriding a decision.
            if (indexer.RequestsPerHour == null)
            {
                indexer.RequestsPerHour = AudiobookBayDefinition.DefaultRequestsPerHour;
                _logger.LogInformation(
                    "Gave {Name} a budget of {Budget} requests per hour, which the patch's {Pages} pages per search spend against",
                    indexer.Name,
                    AudiobookBayDefinition.DefaultRequestsPerHour,
                    pages);
            }

            indexer.UpdatedAt = DateTime.UtcNow;
            await _indexerRepository.UpdateAsync(indexer, ct);

            _logger.LogInformation(
                "Repointed indexer {Name} from {Previous} to the paginated AudioBook Bay definition",
                indexer.Name,
                LogRedaction.SanitizeUrl(previousUrl));

            // Recorded before anything can go wrong later: without the replaced URL and the id of
            // the indexer just created, a revert would have to guess at both.
            await RecordPatchStateAsync(
                indexer.Id,
                previousUrl,
                newIndexerId,
                definitionPath,
                pages,
                Trimmed(definitionsDirectoryOverride),
                ct);

            return AudiobookBayPatchResult.Succeeded(
                definitionPath ?? "installed by hand",
                newIndexerId,
                indexer.Id,
                indexer.Name,
                pages,
                AudiobookBayDefinition.ResultsPerPage,
                AudiobookBayDefinition.ProjectedResults(pages));
        }

        /// <summary>
        /// Picks the mounted Prowlarr config directory, or returns null when none is usable.
        /// </summary>
        /// <remarks>
        /// A directory only qualifies when it carries Prowlarr's own fingerprint — its
        /// <c>config.xml</c> or an existing <c>Definitions</c> folder. Without that check an
        /// unrelated mount could be filled with indexer definitions that nothing ever reads.
        /// </remarks>
        /// <summary>
        /// Puts the indexer back the way it was: the original URL, the created Prowlarr indexer
        /// removed, and optionally the definition file deleted.
        /// </summary>
        /// <remarks>
        /// Refuses rather than guesses when nothing was recorded. A patch applied by hand, or by a
        /// build older than this bookkeeping, leaves no way to know which indexer the operator was
        /// using before, and picking the wrong one would silently stop AudioBook Bay searches.
        ///
        /// The definition file is kept by default: leaving it costs nothing and makes re-applying
        /// immediate, since Prowlarr already has it loaded.
        /// </remarks>
        public async Task<AudiobookBayRevertResult> RevertAsync(bool deleteDefinition = false, CancellationToken ct = default)
        {
            var settings = await _settingsRepository.GetAsync(ct);

            if (settings?.AudiobookBayPatchIndexerId == null
                || string.IsNullOrWhiteSpace(settings.AudiobookBayPatchPreviousIndexerUrl))
            {
                return AudiobookBayRevertResult.NothingRecorded(
                    "Bookmarkarr has no record of applying the patch on this instance, so there is nothing it can safely undo. " +
                    "Point the indexer back at Prowlarr's compiled AudioBook Bay by hand in Settings > Indexers.");
            }

            var indexer = await _indexerRepository.GetByIdAsync(settings.AudiobookBayPatchIndexerId.Value, ct);
            if (indexer == null)
            {
                await ClearPatchStateAsync(ct);
                return AudiobookBayRevertResult.Failed(
                    "The patched indexer no longer exists. Cleared the stored patch record; nothing else to undo.");
            }

            var restoredUrl = settings.AudiobookBayPatchPreviousIndexerUrl!;
            indexer.Url = restoredUrl;
            indexer.UpdatedAt = DateTime.UtcNow;
            await _indexerRepository.UpdateAsync(indexer, ct);

            var prowlarrDeleted = false;
            if (settings.AudiobookBayPatchProwlarrIndexerId is int prowlarrIndexerId)
            {
                prowlarrDeleted = await TryDeleteProwlarrIndexerAsync(prowlarrIndexerId, ct);
            }

            var definitionDeleted = false;
            var definitionPath = Trimmed(settings.AudiobookBayPatchDefinitionPath);
            if (deleteDefinition && definitionPath != null)
            {
                try
                {
                    if (_fileSystem.FileExists(definitionPath))
                    {
                        _fileSystem.DeleteFile(definitionPath);
                        definitionDeleted = true;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
                {
                    // The indexer is already back on its original target, which is the part that
                    // matters; a definition Prowlarr no longer has an indexer for is inert.
                    _logger.LogWarning(ex, "Could not delete the AudioBook Bay definition at {Path}", definitionPath);
                }
            }

            await ClearPatchStateAsync(ct);

            _logger.LogInformation(
                "Reverted the AudioBook Bay patch on indexer {Name}: restored {Url}, Prowlarr indexer deleted {Deleted}",
                indexer.Name,
                LogRedaction.SanitizeUrl(restoredUrl),
                prowlarrDeleted);

            return AudiobookBayRevertResult.Succeeded(
                indexer.Id,
                indexer.Name,
                restoredUrl,
                prowlarrDeleted,
                definitionDeleted);
        }

        /// <summary>
        /// Checks a candidate Prowlarr config directory and, when it is usable, remembers it.
        /// </summary>
        /// <remarks>
        /// Answering before the operator commits is the point: applying the patch is the only way
        /// to discover a bad path today, and it fails after the interesting work has started.
        /// </remarks>
        public async Task<AudiobookBayDirectoryProbe> ProbeDirectoryAsync(string path, CancellationToken ct = default)
        {
            var candidate = Trimmed(path);
            if (candidate == null)
            {
                return new AudiobookBayDirectoryProbe(string.Empty, false, false, false, "Enter a path.");
            }

            bool exists;
            try
            {
                exists = _fileSystem.DirectoryExists(candidate);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new AudiobookBayDirectoryProbe(candidate, false, false, false, $"Not readable from Bookmarkarr: {ex.Message}");
            }

            if (!exists)
            {
                return new AudiobookBayDirectoryProbe(
                    candidate,
                    false,
                    false,
                    false,
                    "No such directory inside Bookmarkarr's container. This is the container-side path, so it only exists if Prowlarr's config is mounted there.");
            }

            if (!Qualifies(candidate))
            {
                return new AudiobookBayDirectoryProbe(
                    candidate,
                    true,
                    false,
                    false,
                    "The directory exists but holds neither config.xml nor a Definitions folder, so it does not look like Prowlarr's config directory.");
            }

            var write = TestWrite(candidate);
            if (write.Writable)
            {
                await SaveDefinitionsDirectoryAsync(candidate, ct);
            }

            return new AudiobookBayDirectoryProbe(
                candidate,
                true,
                true,
                write.Writable,
                write.Writable ? null : write.Message);
        }

        /// <summary>
        /// Searches AudioBook Bay through Prowlarr to see whether the site answers at all.
        /// </summary>
        /// <remarks>
        /// The only check that proves the User-Agent situation. AudioBook Bay serves a 404 to
        /// clients identifying as Prowlarr, so a stock Prowlarr loads this definition, reports the
        /// indexer healthy, and returns nothing for every search.
        /// </remarks>
        public async Task<AudiobookBaySearchProbe> ProbeSearchAsync(string? query, CancellationToken ct = default)
        {
            const string DefaultQuery = "the";
            var term = Trimmed(query) ?? DefaultQuery;

            var diagnostics = await GetDiagnosticsAsync(ct);
            if (diagnostics.ProwlarrIndexerId == null)
            {
                return new AudiobookBaySearchProbe(false, 0, term, "No AudioBook Bay indexer to search through yet.");
            }

            var connection = await _configurationService.GetProwlarrImportSettingsAsync(includeSecret: true);
            var baseUrl = ProwlarrImportUrlPlanner.BuildBaseUrl(connection.Url, connection.Port);

            try
            {
                using var results = await GetJsonAsync(
                    $"{baseUrl}/api/v1/search?query={Uri.EscapeDataString(term)}&indexerIds={diagnostics.ProwlarrIndexerId.Value}&type=search",
                    connection.ApiKey!,
                    ct);

                if (results == null || results.RootElement.ValueKind != JsonValueKind.Array)
                {
                    return new AudiobookBaySearchProbe(false, 0, term, "Prowlarr did not return a result list.");
                }

                var count = results.RootElement.GetArrayLength();
                return new AudiobookBaySearchProbe(
                    true,
                    count,
                    term,
                    count == 0
                        ? "Prowlarr searched and found nothing. If the site is up, this is what a stock Prowlarr looks like: AudioBook Bay 404s its User-Agent. Use a fork that substitutes one, such as prowlarr-abb."
                        : null);
            }
            catch (Exception ex) when (IsTransportFailure(ex) || ex is JsonException)
            {
                _logger.LogWarning(ex, "AudioBook Bay search probe failed");
                return new AudiobookBaySearchProbe(false, 0, term, $"The search did not complete: {ex.Message}");
            }
        }
    }
}
