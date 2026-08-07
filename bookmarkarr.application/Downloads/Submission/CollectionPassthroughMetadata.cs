/*
 * Bookmarkarr - Audiobook Management System
 * Copyright (C) 2024-2026 Bookmarkarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Bookmarkarr.Domain.Downloads;

namespace Bookmarkarr.Application.Downloads.Submission
{
    /// <summary>
    /// Marks a download the user grabbed as a whole collection rather than as one book's release.
    /// </summary>
    /// <remarks>
    /// A collection grab deliberately carries no <c>AudiobookId</c>. The book that was on screen
    /// during the search is a search vehicle only: attaching the download to it would make
    /// <c>AutomaticSearchService</c> treat that book as having an active download and skip
    /// searching it for as long as the collection sat in the queue — the same silent
    /// search-suppression failure as a stale handoff row.
    ///
    /// Without a marker, though, a null <c>AudiobookId</c> is indistinguishable from a corrupt row,
    /// which the processor rejects outright. This flag is what makes "intentionally detached"
    /// legible. It rides in the existing metadata bag so no schema migration is required.
    /// </remarks>
    public static class CollectionPassthroughMetadata
    {
        public const string Key = "CollectionPassthrough";

        /// <summary>
        /// The book whose manual search produced this grab, recorded so it can be marked finished
        /// once the collection lands.
        /// </summary>
        /// <remarks>
        /// Kept out of <c>AudiobookId</c> on purpose. Attaching it there would make the collection
        /// count as that book's active download and stop it being searched for as long as the
        /// download ran; here it is inert until import.
        /// </remarks>
        public const string SourceAudiobookKey = "CollectionSourceAudiobookId";

        public static int? SourceAudiobookId(this Download download)
        {
            var raw = download.GetMetadataString(SourceAudiobookKey);
            return int.TryParse(raw, out var id) && id > 0 ? id : null;
        }

        public static void MarkAsCollection(this Download download) =>
            download.SetMetadata(Key, bool.TrueString);

        public static bool IsCollectionPassthrough(this Download download) =>
            string.Equals(download.GetMetadataString(Key), bool.TrueString, StringComparison.OrdinalIgnoreCase);
    }
}
