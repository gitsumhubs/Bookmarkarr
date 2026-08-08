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
    public sealed class AudiobookBayPatchWorkflow
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

        private static readonly TimeSpan DefinitionReloadTimeout = TimeSpan.FromSeconds(45);
        private static readonly TimeSpan DefinitionPollInterval = TimeSpan.FromSeconds(3);

        private readonly IIndexerRepository _indexerRepository;
        private readonly IConfigurationService _configurationService;
        private readonly IFileSystem _fileSystem;
        private readonly HttpClient _httpClient;
        private readonly ILogger<AudiobookBayPatchWorkflow> _logger;

        public AudiobookBayPatchWorkflow(
            IIndexerRepository indexerRepository,
            IConfigurationService configurationService,
            IFileSystem fileSystem,
            HttpClient httpClient,
            ILogger<AudiobookBayPatchWorkflow> logger)
        {
            _indexerRepository = indexerRepository;
            _configurationService = configurationService;
            _fileSystem = fileSystem;
            _httpClient = httpClient;
            _logger = logger;
        }

        /// <summary>
        /// Reports whether a Bookmarkarr indexer is backed by Prowlarr's single-page AudioBook Bay
        /// implementation, and whether this instance could apply the patch itself.
        /// </summary>
        public async Task<AudiobookBayPatchStatus> GetStatusAsync(CancellationToken ct = default)
        {
            var connection = await _configurationService.GetProwlarrImportSettingsAsync(includeSecret: true);
            if (string.IsNullOrWhiteSpace(connection.Url) || string.IsNullOrWhiteSpace(connection.ApiKey))
            {
                return AudiobookBayPatchStatus.NotApplicable("Prowlarr is not configured");
            }

            var baseUrl = ProwlarrImportUrlPlanner.BuildBaseUrl(connection.Url, connection.Port);
            if (!OutboundRequestSecurity.TryValidateExternalHttpUrl(baseUrl, out var reason, allowPrivateTargets: true))
            {
                return AudiobookBayPatchStatus.NotApplicable($"Blocked Prowlarr target: {reason}");
            }

            JsonDocument? prowlarrIndexers;
            try
            {
                prowlarrIndexers = await GetJsonAsync($"{baseUrl}/api/v1/indexer", connection.ApiKey!, ct);
            }
            catch (Exception ex) when (IsTransportFailure(ex))
            {
                _logger.LogWarning(ex, "Could not reach Prowlarr at {Url} while checking AudioBook Bay", LogRedaction.SanitizeUrl(baseUrl));
                return AudiobookBayPatchStatus.NotApplicable("Could not reach Prowlarr");
            }

            if (prowlarrIndexers == null)
            {
                return AudiobookBayPatchStatus.NotApplicable("Could not read Prowlarr indexers");
            }

            using (prowlarrIndexers)
            {
                if (prowlarrIndexers.RootElement.ValueKind != JsonValueKind.Array)
                {
                    return AudiobookBayPatchStatus.NotApplicable("Unexpected Prowlarr response");
                }

                var compiledIds = new List<int>();
                foreach (var element in prowlarrIndexers.RootElement.EnumerateArray())
                {
                    if (!element.TryGetProperty("id", out var idProp) || idProp.ValueKind != JsonValueKind.Number)
                    {
                        continue;
                    }

                    var implementation = element.TryGetProperty("implementation", out var implProp) && implProp.ValueKind == JsonValueKind.String
                        ? implProp.GetString()
                        : null;

                    if (string.Equals(implementation, AudiobookBayDefinition.CompiledImplementation, StringComparison.OrdinalIgnoreCase))
                    {
                        compiledIds.Add(idProp.GetInt32());
                    }
                }

                if (compiledIds.Count == 0)
                {
                    return AudiobookBayPatchStatus.NotApplicable("No single-page AudioBook Bay indexer found");
                }

                // Only worth surfacing when Bookmarkarr actually searches through it.
                var bookmarkarrIndexers = await _indexerRepository.GetAllAsync(ct);
                foreach (var prowlarrId in compiledIds)
                {
                    var expectedUrl = ProwlarrImportUrlPlanner.NormalizeProxyUrl(
                        ProwlarrImportUrlPlanner.BuildProxyUrl(baseUrl, prowlarrId));

                    var match = bookmarkarrIndexers.FirstOrDefault(i =>
                        i.IsEnabled &&
                        string.Equals(
                            ProwlarrImportUrlPlanner.NormalizeProxyUrl(i.Url),
                            expectedUrl,
                            StringComparison.OrdinalIgnoreCase));

                    if (match != null)
                    {
                        var directory = ResolveDefinitionsDirectory(null);
                        return AudiobookBayPatchStatus.Available(
                            match.Id,
                            match.Name,
                            prowlarrId,
                            AudiobookBayDefinition.ResultsPerPage,
                            AudiobookBayDefinition.ProjectedResults(AudiobookBayDefinition.DefaultPages),
                            directory != null,
                            directory);
                    }
                }

                return AudiobookBayPatchStatus.NotApplicable("AudioBook Bay is not used by any enabled indexer");
            }
        }

        /// <summary>
        /// Writes the paginated definition, waits for Prowlarr to load it, creates the indexer, and
        /// repoints Bookmarkarr's record at it.
        /// </summary>
        public async Task<AudiobookBayPatchResult> ApplyAsync(
            int pages,
            string? definitionsDirectoryOverride,
            CancellationToken ct = default)
        {
            var status = await GetStatusAsync(ct);
            if (!status.PatchAvailable)
            {
                return AudiobookBayPatchResult.Failed(status.Reason ?? "AudioBook Bay patch does not apply here");
            }

            var directory = ResolveDefinitionsDirectory(definitionsDirectoryOverride);
            if (directory == null)
            {
                return AudiobookBayPatchResult.MountRequired(
                    definitionsDirectoryOverride == null
                        ? "Prowlarr's config directory is not mounted into Bookmarkarr"
                        : $"'{definitionsDirectoryOverride}' does not look like a Prowlarr config directory");
            }

            var connection = await _configurationService.GetProwlarrImportSettingsAsync(includeSecret: true);
            var baseUrl = ProwlarrImportUrlPlanner.BuildBaseUrl(connection.Url, connection.Port);
            var apiKey = connection.ApiKey!;

            var customDirectory = Path.Combine(directory, AudiobookBayDefinition.CustomSubdirectory);
            var definitionPath = Path.Combine(customDirectory, AudiobookBayDefinition.FileName);

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

            // Prowlarr rescans the definitions directory on its own; poll rather than restart it.
            var loaded = await WaitForDefinitionAsync(baseUrl, apiKey, ct);
            if (!loaded)
            {
                return AudiobookBayPatchResult.Failed(
                    $"Prowlarr did not pick up the definition within {DefinitionReloadTimeout.TotalSeconds:0} seconds. " +
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
            indexer.UpdatedAt = DateTime.UtcNow;
            await _indexerRepository.UpdateAsync(indexer, ct);

            _logger.LogInformation(
                "Repointed indexer {Name} from {Previous} to the paginated AudioBook Bay definition",
                indexer.Name,
                LogRedaction.SanitizeUrl(previousUrl));

            return AudiobookBayPatchResult.Succeeded(
                definitionPath,
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
        private string? ResolveDefinitionsDirectory(string? overridePath)
        {
            var candidates = string.IsNullOrWhiteSpace(overridePath)
                ? CandidateConfigDirectories
                : [overridePath.Trim()];

            foreach (var candidate in candidates)
            {
                try
                {
                    if (!_fileSystem.DirectoryExists(candidate))
                    {
                        continue;
                    }

                    var looksLikeProwlarr =
                        _fileSystem.FileExists(Path.Combine(candidate, "config.xml")) ||
                        _fileSystem.DirectoryExists(Path.Combine(candidate, "Definitions"));

                    if (looksLikeProwlarr)
                    {
                        return candidate;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // An unreadable candidate is simply not the one.
                }
            }

            return null;
        }

        private async Task<bool> WaitForDefinitionAsync(string baseUrl, string apiKey, CancellationToken ct)
        {
            var deadline = DateTime.UtcNow + DefinitionReloadTimeout;

            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    using var schema = await GetJsonAsync($"{baseUrl}/api/v1/indexer/schema", apiKey, ct);
                    if (schema != null && FindDefinitionSchema(schema.RootElement) != null)
                    {
                        return true;
                    }
                }
                catch (Exception ex) when (IsTransportFailure(ex))
                {
                    _logger.LogDebug(ex, "Prowlarr schema poll failed; retrying");
                }

                await Task.Delay(DefinitionPollInterval, ct);
            }

            return false;
        }

        private async Task<int> CreateProwlarrIndexerAsync(string baseUrl, string apiKey, CancellationToken ct)
        {
            using var schema = await GetJsonAsync($"{baseUrl}/api/v1/indexer/schema", apiKey, ct)
                ?? throw new InvalidOperationException("Prowlarr returned no indexer schema");

            var definition = FindDefinitionSchema(schema.RootElement)
                ?? throw new InvalidOperationException($"Prowlarr does not list '{AudiobookBayDefinition.IndexerName}'");

            var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(definition.GetRawText())
                ?? throw new InvalidOperationException("Prowlarr returned an unreadable indexer schema");

            payload["enable"] = JsonSerializer.SerializeToElement(true);
            if (!payload.TryGetValue("appProfileId", out var profile) || profile.ValueKind != JsonValueKind.Number || profile.GetInt32() <= 0)
            {
                payload["appProfileId"] = JsonSerializer.SerializeToElement(1);
            }

            var body = JsonSerializer.Serialize(payload);
            using var response = await SendAsync(
                () =>
                {
                    var message = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/v1/indexer")
                    {
                        Content = new StringContent(body, Encoding.UTF8, new MediaTypeHeaderValue("application/json"))
                    };
                    message.Headers.Add("X-Api-Key", apiKey);
                    return message;
                },
                $"{baseUrl}/api/v1/indexer",
                ct);

            var created = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Prowlarr returned {(int)response.StatusCode}: {LogRedaction.SanitizeText(created)}");
            }

            using var createdDoc = JsonDocument.Parse(created);
            if (!createdDoc.RootElement.TryGetProperty("id", out var idProp) || idProp.ValueKind != JsonValueKind.Number)
            {
                throw new InvalidOperationException("Prowlarr created the indexer but returned no id");
            }

            return idProp.GetInt32();
        }

        private static JsonElement? FindDefinitionSchema(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var element in root.EnumerateArray())
            {
                var name = element.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String
                    ? nameProp.GetString()
                    : null;

                if (string.Equals(name, AudiobookBayDefinition.IndexerName, StringComparison.OrdinalIgnoreCase))
                {
                    return element;
                }
            }

            return null;
        }

        private async Task<JsonDocument?> GetJsonAsync(string url, string apiKey, CancellationToken ct)
        {
            using var response = await SendAsync(
                () =>
                {
                    var message = new HttpRequestMessage(HttpMethod.Get, url);
                    message.Headers.Add("X-Api-Key", apiKey);
                    return message;
                },
                url,
                ct);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            return JsonDocument.Parse(body);
        }

        private async Task<HttpResponseMessage> SendAsync(
            Func<HttpRequestMessage> requestFactory,
            string url,
            CancellationToken ct)
        {
            var (response, _) = await OutboundRequestSecurity.SendWithValidatedRedirectsAsync(
                _ => requestFactory(),
                new Uri(url),
                _httpClient,
                _logger,
                allowPrivateTargets: true,
                cancellationToken: ct);
            return response;
        }

        private static bool IsTransportFailure(Exception ex) =>
            ex is HttpRequestException or TaskCanceledException or UriFormatException or WebException;
    }
}
