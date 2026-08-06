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

namespace Bookmarkarr.Application.Downloads.Submission
{
    internal static class DownloadRecordFactory
    {
        public static Download CreateQueuedDownload(
            string downloadId,
            TrustedDownloadCandidate candidate,
            PreparedDownloadSubmission submission,
            DownloadClientConfiguration downloadClient,
            string downloadClientId,
            int? audiobookId,
            int? editionId,
            SearchContentType contentType = SearchContentType.Audiobook)
        {
            return new Download
            {
                Id = downloadId,
                AudiobookId = audiobookId,
                EditionId = editionId,
                Title = candidate.Title,
                Artist = candidate.Artist,
                Album = candidate.Album,
                Language = candidate.Language,
                ReleaseGuid = candidate.Id,
                IndexerId = candidate.SourceDescriptor.IndexerId,
                OriginalUrl = submission.OriginalLocator,
                Progress = 0,
                TotalSize = candidate.Size,
                DownloadedSize = 0,
                DownloadPath = downloadClient.DownloadPath ?? string.Empty,
                FinalPath = string.Empty,
                StartedAt = DateTime.UtcNow,
                DownloadClientId = downloadClientId,
                Metadata = new Dictionary<string, object>
                {
                    ["Source"] = candidate.Source,
                    ["Seeders"] = candidate.Seeders ?? 0,
                    ["Quality"] = candidate.Quality ?? string.Empty,
                    ["Language"] = candidate.Language ?? string.Empty,
                    ["DownloadType"] = submission.Protocol.ToString(),
                    ["ContentType"] = contentType == SearchContentType.Ebook
                        ? DownloadContentTypes.Ebook
                        : DownloadContentTypes.Audiobook
                }
            };
        }
    }
}
