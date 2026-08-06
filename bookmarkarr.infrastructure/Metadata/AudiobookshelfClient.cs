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

using System.Net.Http.Headers;
using System.Text.Json;
using Bookmarkarr.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Bookmarkarr.Infrastructure.Metadata
{
    public sealed class AudiobookshelfClient(
        HttpClient httpClient,
        IConfigurationService configurationService,
        ILogger<AudiobookshelfClient> logger) : IAudiobookshelfClient
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        private ApplicationSettings? _settings;

        public bool IsConfigured
        {
            get
            {
                var settings = LoadSettings();
                return !string.IsNullOrWhiteSpace(settings?.AudiobookshelfUrl)
                    && !string.IsNullOrWhiteSpace(settings?.AudiobookshelfApiKey);
            }
        }

        public async Task<(bool Success, string Message)> TestConnectionAsync(CancellationToken ct = default)
        {
            var settings = await configurationService.GetApplicationSettingsAsync();
            if (string.IsNullOrWhiteSpace(settings.AudiobookshelfUrl))
            {
                return (false, "No Audiobookshelf URL configured");
            }

            try
            {
                using var request = BuildRequest(settings, "/api/libraries");
                using var response = await httpClient.SendAsync(request, ct);

                if (!response.IsSuccessStatusCode)
                {
                    return (false, $"Audiobookshelf returned {(int)response.StatusCode}");
                }

                var libraries = await ReadLibrariesAsync(response, ct);
                return (true, $"Connected — {libraries.Count} librar{(libraries.Count == 1 ? "y" : "ies")} visible");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return (false, exception.Message);
            }
        }

        public async Task<IReadOnlyDictionary<string, AudiobookshelfItem>> GetLibraryIndexAsync(
            CancellationToken ct = default)
        {
            var index = new Dictionary<string, AudiobookshelfItem>(StringComparer.OrdinalIgnoreCase);

            var settings = await configurationService.GetApplicationSettingsAsync();
            if (string.IsNullOrWhiteSpace(settings.AudiobookshelfUrl)
                || string.IsNullOrWhiteSpace(settings.AudiobookshelfApiKey))
            {
                return index;
            }

            try
            {
                using var librariesRequest = BuildRequest(settings, "/api/libraries");
                using var librariesResponse = await httpClient.SendAsync(librariesRequest, ct);
                librariesResponse.EnsureSuccessStatusCode();

                foreach (var libraryId in await ReadLibrariesAsync(librariesResponse, ct))
                {
                    await AddLibraryItemsAsync(settings, libraryId, index, ct);
                }

                logger.LogInformation("Audiobookshelf index built with {Count} item(s)", index.Count);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Degrades to folder-name matching rather than failing the scan: Audiobookshelf is
                // an accelerator here, never a dependency.
                logger.LogWarning(exception, "Could not read Audiobookshelf; continuing without it");
            }

            return index;
        }

        private async Task AddLibraryItemsAsync(
            ApplicationSettings settings,
            string libraryId,
            Dictionary<string, AudiobookshelfItem> index,
            CancellationToken ct)
        {
            // `limit=0` asks for everything in one response; the endpoint otherwise pages at 100
            // and a large library would need many round trips.
            using var request = BuildRequest(settings, $"/api/libraries/{libraryId}/items?limit=0");
            using var response = await httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!document.RootElement.TryGetProperty("results", out var results))
            {
                return;
            }

            foreach (var element in results.EnumerateArray())
            {
                var item = ParseItem(element);
                if (item is not null)
                {
                    index[NormalizePath(item.Path)] = item;
                }
            }
        }

        private static AudiobookshelfItem? ParseItem(JsonElement element)
        {
            var path = element.TryGetProperty("path", out var pathElement) ? pathElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            if (!element.TryGetProperty("media", out var media)
                || !media.TryGetProperty("metadata", out var metadata))
            {
                return new AudiobookshelfItem(path, null, [], null, null, null, null);
            }

            var authors = new List<string>();
            if (metadata.TryGetProperty("authors", out var authorsElement)
                && authorsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var author in authorsElement.EnumerateArray())
                {
                    var name = author.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        authors.Add(name);
                    }
                }
            }
            else if (metadata.TryGetProperty("authorName", out var authorName))
            {
                // Audiobookshelf's "minified" shape flattens authors to a single string.
                var name = authorName.GetString();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    authors.Add(name);
                }
            }

            string? series = null;
            string? sequence = null;
            if (metadata.TryGetProperty("series", out var seriesElement))
            {
                var first = seriesElement.ValueKind == JsonValueKind.Array
                    ? seriesElement.EnumerateArray().FirstOrDefault()
                    : seriesElement;

                if (first.ValueKind == JsonValueKind.Object)
                {
                    series = first.TryGetProperty("name", out var seriesName) ? seriesName.GetString() : null;
                    sequence = first.TryGetProperty("sequence", out var sequenceElement)
                        ? sequenceElement.GetString()
                        : null;
                }
            }

            return new AudiobookshelfItem(
                path,
                GetString(metadata, "title"),
                authors,
                series,
                sequence,
                GetString(metadata, "asin"),
                GetString(metadata, "publishedYear"));
        }

        private static string? GetString(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        private static async Task<List<string>> ReadLibrariesAsync(HttpResponseMessage response, CancellationToken ct)
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!document.RootElement.TryGetProperty("libraries", out var libraries))
            {
                return [];
            }

            return [.. libraries.EnumerateArray()
                .Select(library => library.TryGetProperty("id", out var id) ? id.GetString() : null)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)];
        }

        private static HttpRequestMessage BuildRequest(ApplicationSettings settings, string relativePath)
        {
            var baseUrl = settings.AudiobookshelfUrl!.TrimEnd('/');
            var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}{relativePath}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.AudiobookshelfApiKey);
            return request;
        }

        /// <summary>
        /// Trailing separators and casing vary between what Audiobookshelf reports and what a
        /// filesystem walk produces, so both sides are flattened before they are compared.
        /// </summary>
        internal static string NormalizePath(string path) =>
            FileUtils.NormalizeStoredPath(path ?? string.Empty)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        private ApplicationSettings? LoadSettings()
        {
            try
            {
                _settings ??= configurationService.GetApplicationSettingsAsync().GetAwaiter().GetResult();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogDebug(exception, "Could not read settings while checking Audiobookshelf configuration");
            }

            return _settings;
        }
    }
}
