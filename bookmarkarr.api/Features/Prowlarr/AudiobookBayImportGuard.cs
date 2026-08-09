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

using System.Text.Json;

namespace Bookmarkarr.Api.Features.Prowlarr
{
    /// <summary>
    /// Keeps an indexer import from undoing the AudioBook Bay pagination patch.
    /// </summary>
    /// <remarks>
    /// Prowlarr holds both AudioBook Bay indexers at once — its compiled one and the paginated
    /// Cardigann definition the patch installs — and both carry the audiobook category the import
    /// filters on. An operator who imports after patching would get the compiled indexer back as a
    /// second, enabled indexer, and searches would fan out to the nine-result cap again.
    /// </remarks>
    internal static class AudiobookBayImportGuard
    {
        /// <summary>
        /// True when this Prowlarr indexer is the compiled AudioBook Bay implementation.
        /// </summary>
        public static bool IsCompiledAudiobookBay(JsonElement element)
            => string.Equals(
                ReadString(element, "implementation"),
                AudiobookBayDefinition.CompiledImplementation,
                StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// True when an enabled Bookmarkarr indexer already searches the paginated definition.
        /// </summary>
        /// <remarks>
        /// The indexer's display name belongs to the operator and may be anything, so the paginated
        /// indexer is recognised by its Cardigann definition name — the file the patch wrote.
        /// </remarks>
        public static bool PatchedIndexerInUse(JsonElement prowlarrIndexers, IEnumerable<Indexer> bookmarkarrIndexers, string baseUrl)
        {
            if (prowlarrIndexers.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var enabledUrls = bookmarkarrIndexers
                .Where(i => i.IsEnabled)
                .Select(i => ProwlarrImportUrlPlanner.NormalizeProxyUrl(i.Url))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var element in prowlarrIndexers.EnumerateArray())
            {
                if (!element.TryGetProperty("id", out var idProp) || idProp.ValueKind != JsonValueKind.Number)
                {
                    continue;
                }

                var isPaginatedDefinition =
                    string.Equals(ReadString(element, "implementation"), AudiobookBayDefinition.CardigannImplementation, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(ReadString(element, "definitionName"), AudiobookBayDefinition.DefinitionFileStem, StringComparison.OrdinalIgnoreCase);

                if (!isPaginatedDefinition)
                {
                    continue;
                }

                var expectedUrl = ProwlarrImportUrlPlanner.NormalizeProxyUrl(
                    ProwlarrImportUrlPlanner.BuildProxyUrl(baseUrl, idProp.GetInt32()));

                if (enabledUrls.Contains(expectedUrl))
                {
                    return true;
                }
            }

            return false;
        }

        private static string? ReadString(JsonElement element, string propertyName)
            => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }
}
