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
using System.Text.Json.Serialization;
using Bookmarkarr.Domain.Common;

namespace Bookmarkarr.Domain.Downloads
{
    public enum DownloadStatus
    {
        Queued,
        Downloading,
        Paused,
        Completed,
        Failed,
        Processing,
        Ready,
        Moved,           // Added to track completed moves
        ImportBlocked,   // Added: Block re-processing of bad imports
        ImportPending,   // Added: Explicitly waiting on import completion/manual interaction
        /// <summary>
        /// Import history remains, but the completed payload is no longer exposed by its
        /// download client. This is terminal until reconciliation sees the source again.
        /// </summary>
        SourceMissing,
        /// <summary>
        /// The client reports a file that is not on disk under that name, but a file whose name
        /// matches after the client's own mangling is sitting beside it. Retrying cannot help —
        /// the name will never change on its own — so this waits for the operator to confirm the
        /// match rather than burning retries into <see cref="ImportBlocked"/>.
        /// </summary>
        ImportNameMismatch
    }
}
