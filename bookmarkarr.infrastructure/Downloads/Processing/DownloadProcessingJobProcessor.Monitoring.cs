/*
 * Bookmarkarr - Audiobook Management System
 * Copyright (C) 2024-2026 Bookmarkarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Bookmarkarr.Domain.Books;
using Bookmarkarr.Domain.Configuration;

namespace Bookmarkarr.Infrastructure.Downloads.Processing
{
    public partial class DownloadProcessingJobProcessor
    {
        /// <summary>
        /// Marks an edition imported, and stops monitoring it when it holds files and the operator
        /// has asked for that.
        /// </summary>
        /// <remarks>
        /// An edition that keeps its monitor after import keeps being searched every six hours in
        /// the hope of an upgrade, whether or not anyone wants one. That is the *arr default and it
        /// is harmless against a private tracker; against a site that bans for request volume it is
        /// the entire bill. Opt-in, because it does turn off upgrade searching.
        ///
        /// The file check matters: an edition marked imported with nothing on disk is a failed
        /// import, and retiring its monitor would strand it with no way back.
        /// </remarks>
        private static void MarkEditionImported(BookEdition edition, ApplicationSettings? settings)
        {
            edition.Status = EditionWantedStatus.Imported;

            if (settings?.UnmonitorImportedEditions == true && edition.Files.Count > 0)
            {
                edition.Monitored = false;
            }

            edition.UpdatedAt = DateTime.UtcNow;
        }
    }
}
