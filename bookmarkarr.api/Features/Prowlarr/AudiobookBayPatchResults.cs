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

    /// <summary>How a single precondition turned out.</summary>
    public enum AudiobookBayCheckState
    {
        /// <summary>The precondition holds.</summary>
        Pass,

        /// <summary>The precondition does not hold and the patch cannot proceed because of it.</summary>
        Fail,

        /// <summary>Worth knowing, but not a blocker.</summary>
        Warn,

        /// <summary>Could not be determined, usually because an earlier check failed.</summary>
        Unknown
    }

    /// <summary>Whether this instance currently searches AudioBook Bay through the patched definition.</summary>
    public enum AudiobookBayPatchState
    {
        /// <summary>An enabled indexer points at the paginated definition.</summary>
        Patched,

        /// <summary>An enabled indexer points at Prowlarr's compiled, single-page indexer.</summary>
        NotPatched,

        /// <summary>No enabled indexer searches AudioBook Bay at all.</summary>
        NotApplicable,

        /// <summary>Prowlarr could not be consulted, so neither answer would be honest.</summary>
        Unknown
    }

    /// <summary>
    /// One precondition, its outcome, and what to do about it.
    /// </summary>
    /// <remarks>
    /// Carrying the remedy alongside the result keeps the advice next to the finding that produced
    /// it, rather than in UI copy that has to re-derive which failure it is looking at.
    /// </remarks>
    public sealed record AudiobookBayCheck(
        string Id,
        string Label,
        AudiobookBayCheckState State,
        string? Detail,
        string? Remedy);

    /// <summary>
    /// The full picture: current state, every precondition, and what can be done from here.
    /// </summary>
    public sealed record AudiobookBayDiagnostics(
        AudiobookBayPatchState PatchState,
        IReadOnlyList<AudiobookBayCheck> Checks,
        int? IndexerId,
        string? IndexerName,
        int? ProwlarrIndexerId,
        string? DefinitionsDirectory,
        bool DefinitionsDirectoryFromOverride,
        bool CanAutoApply,
        bool CanRevert,
        bool DefinitionInstalled,
        int? InstalledPages,
        int CurrentResultCap,
        int ProjectedResults);

    /// <summary>Outcome of probing a candidate Prowlarr configuration directory.</summary>
    public sealed record AudiobookBayDirectoryProbe(
        string Path,
        bool Exists,
        bool LooksLikeProwlarr,
        bool Writable,
        string? Message);

    /// <summary>
    /// Outcome of searching through the AudioBook Bay indexer to see whether the site answers.
    /// </summary>
    /// <remarks>
    /// AudioBook Bay serves a 404 to clients identifying as Prowlarr, so a stock Prowlarr installs
    /// this definition cleanly and then returns nothing. Only a real search distinguishes that from
    /// a working setup, which is why this is a separate, operator-triggered check.
    /// </remarks>
    public sealed record AudiobookBaySearchProbe(
        bool Ran,
        int Results,
        string Query,
        string? Message);

    /// <summary>Outcome of reverting the patch.</summary>
    public sealed record AudiobookBayRevertResult(
        AudiobookBayRevertResultKind Kind,
        string? Message,
        int? IndexerId,
        string? IndexerName,
        string? RestoredUrl,
        bool ProwlarrIndexerDeleted,
        bool DefinitionDeleted)
    {
        public static AudiobookBayRevertResult Succeeded(
            int indexerId,
            string indexerName,
            string restoredUrl,
            bool prowlarrIndexerDeleted,
            bool definitionDeleted)
            => new(AudiobookBayRevertResultKind.Success, null, indexerId, indexerName, restoredUrl, prowlarrIndexerDeleted, definitionDeleted);

        /// <summary>
        /// Nothing was recorded to undo. Distinct from a failure: a patch applied by hand, or by a
        /// build that predates this bookkeeping, leaves nothing safe to reverse automatically.
        /// </summary>
        public static AudiobookBayRevertResult NothingRecorded(string message)
            => new(AudiobookBayRevertResultKind.NothingRecorded, message, null, null, null, false, false);

        public static AudiobookBayRevertResult Failed(string message)
            => new(AudiobookBayRevertResultKind.Failed, message, null, null, null, false, false);
    }

    public enum AudiobookBayRevertResultKind
    {
        Success,
        NothingRecorded,
        Failed
    }
}
