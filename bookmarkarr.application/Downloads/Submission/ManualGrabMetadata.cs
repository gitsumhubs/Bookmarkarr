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
    /// Marks a download as a release a person picked, rather than one scoring chose.
    /// </summary>
    /// <remarks>
    /// Only manual grabs are marked; everything else is automatic by construction, because manual
    /// search is the sole path that sets this. Rows created before the flag existed therefore read
    /// as automatic, which is the safe direction: the alternative is refusing to supersede a
    /// download the user is actively trying to replace. Only in-flight downloads are ever
    /// considered, and those turn over quickly, so the ambiguity is short-lived.
    /// </remarks>
    public static class ManualGrabMetadata
    {
        public const string Key = "GrabOrigin";
        public const string Manual = "manual";

        public static void MarkAsManualGrab(this Download download) =>
            download.SetMetadata(Key, Manual);

        public static bool IsManualGrab(this Download download) =>
            string.Equals(download.GetMetadataString(Key), Manual, StringComparison.OrdinalIgnoreCase);
    }
}
