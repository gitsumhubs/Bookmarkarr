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
    /// Talking to Prowlarr, reading the filesystem, and the small shared helpers the rest
    /// of the workflow is built from.
    /// </summary>
    public sealed partial class AudiobookBayPatchWorkflow
    {
        private async Task<bool> TryDeleteProwlarrIndexerAsync(int prowlarrIndexerId, CancellationToken ct)
        {
            var connection = await _configurationService.GetProwlarrImportSettingsAsync(includeSecret: true);
            if (string.IsNullOrWhiteSpace(connection.Url) || string.IsNullOrWhiteSpace(connection.ApiKey))
            {
                return false;
            }

            var baseUrl = ProwlarrImportUrlPlanner.BuildBaseUrl(connection.Url, connection.Port);
            var url = $"{baseUrl}/api/v1/indexer/{prowlarrIndexerId}";

            try
            {
                using var response = await SendAsync(
                    () =>
                    {
                        var message = new HttpRequestMessage(HttpMethod.Delete, url);
                        message.Headers.Add("X-Api-Key", connection.ApiKey!);
                        return message;
                    },
                    url,
                    ct);

                if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
                {
                    return true;
                }

                _logger.LogWarning(
                    "Prowlarr returned {Status} deleting indexer {Id} during revert",
                    (int)response.StatusCode,
                    prowlarrIndexerId);
                return false;
            }
            catch (Exception ex) when (IsTransportFailure(ex))
            {
                _logger.LogWarning(ex, "Could not delete Prowlarr indexer {Id} during revert", prowlarrIndexerId);
                return false;
            }
        }

        private async Task RecordPatchStateAsync(
            int indexerId,
            string previousUrl,
            int prowlarrIndexerId,
            string? definitionPath,
            int pages,
            string? definitionsDirectory,
            CancellationToken ct)
        {
            var settings = await _settingsRepository.GetAsync(ct) ?? new ApplicationSettings { Id = 1 };

            settings.AudiobookBayPatchIndexerId = indexerId;
            settings.AudiobookBayPatchPreviousIndexerUrl = previousUrl;
            settings.AudiobookBayPatchProwlarrIndexerId = prowlarrIndexerId;
            settings.AudiobookBayPatchDefinitionPath = definitionPath;
            settings.AudiobookBayPatchPages = pages;

            if (definitionsDirectory != null)
            {
                settings.AudiobookBayDefinitionsDirectory = definitionsDirectory;
            }

            await _settingsRepository.SaveAsync(settings, ct);
        }

        private async Task ClearPatchStateAsync(CancellationToken ct)
        {
            var settings = await _settingsRepository.GetAsync(ct);
            if (settings == null)
            {
                return;
            }

            settings.AudiobookBayPatchIndexerId = null;
            settings.AudiobookBayPatchPreviousIndexerUrl = null;
            settings.AudiobookBayPatchProwlarrIndexerId = null;
            settings.AudiobookBayPatchDefinitionPath = null;
            settings.AudiobookBayPatchPages = null;

            // The directory is where Prowlarr lives, not part of the patch, so it survives a revert.
            await _settingsRepository.SaveAsync(settings, ct);
        }

        private async Task SaveDefinitionsDirectoryAsync(string directory, CancellationToken ct)
        {
            var settings = await _settingsRepository.GetAsync(ct) ?? new ApplicationSettings { Id = 1 };
            if (string.Equals(settings.AudiobookBayDefinitionsDirectory, directory, StringComparison.Ordinal))
            {
                return;
            }

            settings.AudiobookBayDefinitionsDirectory = directory;
            await _settingsRepository.SaveAsync(settings, ct);
        }

        /// <summary>
        /// Confirms the definitions directory can actually be written to, by writing to it.
        /// </summary>
        /// <remarks>
        /// A read-only bind mount is indistinguishable from a writable one until something tries,
        /// and that is the common misconfiguration. Checking it here turns a failure that would
        /// otherwise surface as a 45-second reload timeout into a plain answer.
        /// </remarks>
        private (bool Writable, string? Message) TestWrite(string directory)
        {
            var customDirectory = AudiobookBayDefinition.CustomDirectoryIn(directory);
            var probePath = Path.Combine(customDirectory, $".bookmarkarr-write-test-{Guid.NewGuid():N}");

            try
            {
                _fileSystem.CreateDirectory(customDirectory);
                _fileSystem.WriteAllText(probePath, "bookmarkarr write test");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                return (false, $"Could not write to {customDirectory}: {ex.Message}");
            }

            try
            {
                _fileSystem.DeleteFile(probePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // Writable is what was being established; a leftover probe file is cosmetic.
                _logger.LogDebug(ex, "Could not remove the write-test file at {Path}", probePath);
            }

            return (true, null);
        }

        private bool FileExistsSafely(string path)
        {
            try
            {
                return _fileSystem.FileExists(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        private bool DirectoryExistsSafely(string path)
        {
            try
            {
                return _fileSystem.DirectoryExists(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>
        /// Returns the checks in a fixed order, filling in the ones that never ran as Unknown.
        /// </summary>
        private static IReadOnlyList<AudiobookBayCheck> Ordered(List<AudiobookBayCheck> checks)
        {
            var byId = checks.ToDictionary(c => c.Id, StringComparer.Ordinal);

            return CheckSequence
                .Select(entry => byId.TryGetValue(entry.Id, out var check)
                    ? check
                    : Unknown(entry.Id, entry.Label, "Not checked: an earlier step has to succeed first."))
                .ToList();
        }

        private static AudiobookBayCheck Unknown(string id, string label, string? remedy) =>
            new(id, label, AudiobookBayCheckState.Unknown, null, remedy);

        /// <summary>Condenses the diagnostics into the single sentence the startup prompt expects.</summary>
        private static string DescribeState(AudiobookBayPatchState state, IReadOnlyList<AudiobookBayCheck> checks)
        {
            if (state == AudiobookBayPatchState.Patched)
            {
                return "AudioBook Bay is already searched through the paginated definition";
            }

            if (state == AudiobookBayPatchState.NotApplicable)
            {
                return "AudioBook Bay is not used by any enabled indexer";
            }

            var blocker = checks.FirstOrDefault(c => c.State == AudiobookBayCheckState.Fail);
            return blocker?.Detail ?? "AudioBook Bay could not be checked";
        }

        private static string? ReadString(JsonElement element, string property) =>
            element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        private static string? Trimmed(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        /// <param name="overridePath">A path supplied with this request, tried alone when present.</param>
        /// <param name="savedPath">
        /// The path saved by an earlier successful probe. Tried before the conventional mount
        /// points so an operator who has already told Bookmarkarr where Prowlarr lives is not asked
        /// again on the next request.
        /// </param>
        private ResolvedDirectory ResolveDefinitionsDirectory(string? overridePath, string? savedPath)
        {
            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                return new ResolvedDirectory(Qualifies(overridePath.Trim()) ? overridePath.Trim() : null, true);
            }

            if (!string.IsNullOrWhiteSpace(savedPath) && Qualifies(savedPath))
            {
                return new ResolvedDirectory(savedPath, true);
            }

            foreach (var candidate in CandidateConfigDirectories)
            {
                if (Qualifies(candidate))
                {
                    return new ResolvedDirectory(candidate, false);
                }
            }

            return new ResolvedDirectory(null, false);
        }

        /// <summary>
        /// A directory only qualifies when it carries Prowlarr's own fingerprint — its
        /// <c>config.xml</c> or an existing <c>Definitions</c> folder. Without that check an
        /// unrelated mount could be filled with indexer definitions that nothing ever reads.
        /// </summary>
        private bool Qualifies(string candidate)
        {
            try
            {
                if (!_fileSystem.DirectoryExists(candidate))
                {
                    return false;
                }

                return _fileSystem.FileExists(Path.Combine(candidate, "config.xml"))
                    || _fileSystem.DirectoryExists(Path.Combine(candidate, "Definitions"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // An unreadable candidate is simply not the one.
                return false;
            }
        }

        private sealed record ResolvedDirectory(string? Directory, bool FromOverride);

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
