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

namespace Bookmarkarr.Api.Features.Prowlarr
{
    /// <summary>
    /// Whether the AudioBook Bay patch applies to this instance, and whether it can be applied
    /// from here.
    /// </summary>
    public sealed record AudiobookBayPatchStatus(
        bool PatchAvailable,
        string? Reason,
        int? IndexerId,
        string? IndexerName,
        int? ProwlarrIndexerId,
        int CurrentResultCap,
        int ProjectedResults,
        bool CanAutoApply,
        string? DefinitionsDirectory)
    {
        public static AudiobookBayPatchStatus NotApplicable(string reason)
            => new(false, reason, null, null, null, 0, 0, false, null);

        public static AudiobookBayPatchStatus Available(
            int indexerId,
            string indexerName,
            int prowlarrIndexerId,
            int currentResultCap,
            int projectedResults,
            bool canAutoApply,
            string? definitionsDirectory)
            => new(true, null, indexerId, indexerName, prowlarrIndexerId, currentResultCap, projectedResults, canAutoApply, definitionsDirectory);
    }

    /// <summary>Outcome of applying the patch.</summary>
    public sealed record AudiobookBayPatchResult(
        AudiobookBayPatchResultKind Kind,
        string? Message,
        string? DefinitionPath,
        int? ProwlarrIndexerId,
        int? IndexerId,
        string? IndexerName,
        int Pages,
        int PreviousResultCap,
        int ProjectedResults)
    {
        public static AudiobookBayPatchResult Succeeded(
            string definitionPath,
            int prowlarrIndexerId,
            int indexerId,
            string indexerName,
            int pages,
            int previousResultCap,
            int projectedResults)
            => new(AudiobookBayPatchResultKind.Success, null, definitionPath, prowlarrIndexerId, indexerId, indexerName, pages, previousResultCap, projectedResults);

        /// <summary>
        /// Prowlarr's config directory is not reachable from Bookmarkarr. Recoverable by supplying
        /// a path or installing the file by hand, so it is not reported as a failure.
        /// </summary>
        public static AudiobookBayPatchResult MountRequired(string message)
            => new(AudiobookBayPatchResultKind.MountRequired, message, null, null, null, null, 0, 0, 0);

        public static AudiobookBayPatchResult Failed(string message)
            => new(AudiobookBayPatchResultKind.Failed, message, null, null, null, null, 0, 0, 0);
    }

    public enum AudiobookBayPatchResultKind
    {
        Success,
        MountRequired,
        Failed
    }
}
