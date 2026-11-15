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

namespace Bookmarkarr.Domain.Search
{
    /// <summary>
    /// Size limits for a release taken without a person looking at it.
    /// </summary>
    /// <remarks>
    /// A quality profile's maximum only helps if it was set. Profiles made before the 5 GB default
    /// carry zero, meaning no limit, and the unattended pass took an 846 GB collection for one
    /// book. So when the profile sets no maximum there is still a ceiling here, generous enough for
    /// a long book in a heavy format and far below any pack. A profile maximum replaces it in
    /// either direction.
    ///
    /// The floor is for audiobooks only: a few megabytes under a real title is a sample, a single
    /// chapter or a dead post.
    /// </remarks>
    public static class ReleaseSizeGuard
    {
        private const long Megabyte = 1024 * 1024;

        public const int MinimumAudiobookMegabytes = 15;
        public const int UnattendedAudiobookCeilingMegabytes = 6 * 1024;
        public const int UnattendedEbookCeilingMegabytes = 500;

        /// <summary>The most a single book may weigh when nobody is choosing: the profile's maximum, or the built-in ceiling.</summary>
        public static long CeilingBytes(bool ebook, int profileMaximumMegabytes) =>
            (profileMaximumMegabytes > 0
                ? profileMaximumMegabytes
                : ebook ? UnattendedEbookCeilingMegabytes : UnattendedAudiobookCeilingMegabytes) * Megabyte;

        /// <summary>Why the size rules a release out, or null when it is fine or its size is unknown.</summary>
        public static string? Check(long sizeBytes, bool ebook, int profileMaximumMegabytes)
        {
            if (sizeBytes <= 0)
            {
                return null;
            }

            if (!ebook && sizeBytes < MinimumAudiobookMegabytes * Megabyte)
            {
                return $"{sizeBytes / Megabyte} MB is too small to be a whole audiobook";
            }

            if (profileMaximumMegabytes > 0)
            {
                return sizeBytes > profileMaximumMegabytes * Megabyte
                    ? $"{sizeBytes / Megabyte} MB is over the profile maximum of {profileMaximumMegabytes} MB"
                    : null;
            }

            var ceiling = ebook ? UnattendedEbookCeilingMegabytes : UnattendedAudiobookCeilingMegabytes;
            return sizeBytes > ceiling * Megabyte
                ? $"{sizeBytes / Megabyte} MB is over the {ceiling} MB limit for unattended grabs; set a maximum size on the quality profile to change it"
                : null;
        }
    }
}
