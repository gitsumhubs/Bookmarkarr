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

namespace Bookmarkarr.Api.Dtos
{
    public class AudiobookBayPatchRequestDto
    {
        /// <summary>Pages of AudioBook Bay results to read per search. Nine results per page.</summary>
        public int? Pages { get; set; }

        /// <summary>
        /// Prowlarr config directory as mounted inside Bookmarkarr. Left null, the conventional
        /// mount points are probed instead.
        /// </summary>
        public string? DefinitionsDirectory { get; set; }

        /// <summary>
        /// Set when the operator has already copied the definition into Prowlarr themselves, so
        /// the file write is skipped and only the Prowlarr and Bookmarkarr wiring is done.
        /// </summary>
        public bool? DefinitionAlreadyInstalled { get; set; }
    }

    public class AudiobookBayDirectoryProbeRequestDto
    {
        /// <summary>Container-side path to test, for example <c>/prowlarr-config</c>.</summary>
        public string? Path { get; set; }
    }

    public class AudiobookBaySearchProbeRequestDto
    {
        /// <summary>Term to search for. A common word is the point: it should match plenty.</summary>
        public string? Query { get; set; }
    }

    public class AudiobookBayRevertRequestDto
    {
        /// <summary>
        /// Also delete the definition file. Off by default: keeping it makes re-applying immediate
        /// and an unused definition does nothing on its own.
        /// </summary>
        public bool? DeleteDefinition { get; set; }
    }
}
