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

using Bookmarkarr.Application.Downloads.Blocklisting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bookmarkarr.Tests.Features.Application.Downloads.Blocklisting
{
    public class BlocklistServiceTests
    {
        private const int AudiobookId = 42;
        private const string ReleaseTitle = "An.Author-Example.Book.2024.M4B-GROUP";

        [Fact]
        public async Task RecordFailureAsync_DoesNotBlocklist_OnFirstFailure()
        {
            // One failure is routine — a stalled peer, a transient client error. Blocking on it
            // would discard releases that succeed on a plain retry.
            var failed = CreateFailedDownload();
            var service = CreateService([], [failed]);

            var entry = await service.RecordFailureAsync(failed);

            Assert.Null(entry);
        }

        [Fact]
        public async Task RecordFailureAsync_Blocklists_OnSecondFailureOfSameRelease()
        {
            var failed = CreateFailedDownload();
            var earlierAttempt = CreateFailedDownload(id: "download-1");
            var service = CreateService([], [earlierAttempt, failed]);

            var entry = await service.RecordFailureAsync(failed);

            Assert.NotNull(entry);
            Assert.Equal(AudiobookId, entry!.AudiobookId);
            Assert.Equal(ReleaseTitle, entry.Title);
            Assert.Equal(2, entry.FailureCount);
        }

        [Fact]
        public async Task RecordFailureAsync_IgnoresFailuresOfADifferentRelease()
        {
            // A different release failing for the same book must not push this one over the line.
            var failed = CreateFailedDownload();
            var otherRelease = CreateFailedDownload(id: "download-1", title: "An.Author-Example.Book.2024.MP3-OTHER");
            otherRelease.ReleaseGuid = "other-guid";

            var service = CreateService([], [otherRelease, failed]);

            var entry = await service.RecordFailureAsync(failed);

            Assert.Null(entry);
        }

        [Fact]
        public async Task RecordFailureAsync_IgnoresNonFailedDownloads()
        {
            // Only genuine download failures count. An import block means the bytes arrived and
            // Bookmarkarr could not file them, which is a fault on our side, not a bad release.
            var failed = CreateFailedDownload();
            var importBlocked = CreateFailedDownload(id: "download-1");
            importBlocked.Status = DownloadStatus.ImportBlocked;

            var service = CreateService([], [importBlocked, failed]);

            var entry = await service.RecordFailureAsync(failed);

            Assert.Null(entry);
        }

        [Fact]
        public async Task RecordFailureAsync_DoesNotDuplicateAnExistingEntry()
        {
            var failed = CreateFailedDownload();
            var existing = new BlocklistEntry
            {
                Id = 7,
                AudiobookId = AudiobookId,
                ReleaseGuid = failed.ReleaseGuid,
                IndexerId = failed.IndexerId,
                Title = ReleaseTitle,
                NormalizedTitle = ReleaseIdentity.NormalizeReleaseName(ReleaseTitle),
                Size = failed.TotalSize
            };

            var service = CreateService([existing], [CreateFailedDownload(id: "download-1"), failed]);

            var entry = await service.RecordFailureAsync(failed);

            Assert.Equal(7, entry!.Id);
        }

        [Fact]
        public async Task RecordFailureAsync_DoesNothing_WhenTheDownloadHasNoBook()
        {
            // Scope is always one book. Without one there is nothing to attach an entry to, and a
            // global ban would punish an entire indexer for one bad post.
            var failed = CreateFailedDownload();
            failed.AudiobookId = null;

            var service = CreateService([], [failed]);

            Assert.Null(await service.RecordFailureAsync(failed));
        }

        [Fact]
        public async Task BlocklistReleaseAsync_BlocklistsImmediately_WithoutASecondStrike()
        {
            // An external caller that has already watched the download stall across several checks
            // knows more than a single client status poll does, so making it earn a second strike
            // would just mean grabbing the same dead release again.
            var failed = CreateFailedDownload();
            var service = CreateService([], [failed]);

            var entry = await service.BlocklistReleaseAsync(failed, "stalled at 0 B/s");

            Assert.NotNull(entry);
            Assert.Equal(ReleaseTitle, entry!.Title);
            Assert.Equal("stalled at 0 B/s", entry.Reason);
        }

        [Fact]
        public async Task BlocklistReleaseAsync_ReusesAnExistingEntry()
        {
            var failed = CreateFailedDownload();
            var existing = new BlocklistEntry
            {
                Id = 11,
                AudiobookId = AudiobookId,
                ReleaseGuid = failed.ReleaseGuid,
                IndexerId = failed.IndexerId,
                Title = ReleaseTitle,
                NormalizedTitle = ReleaseIdentity.NormalizeReleaseName(ReleaseTitle),
                Size = failed.TotalSize
            };

            var service = CreateService([existing], [failed]);

            var entry = await service.BlocklistReleaseAsync(failed, "stalled");

            Assert.Equal(11, entry!.Id);
        }

        [Fact]
        public async Task BlocklistReleaseAsync_DoesNothing_WhenTheDownloadHasNoBook()
        {
            var failed = CreateFailedDownload();
            failed.AudiobookId = null;

            var service = CreateService([], [failed]);

            Assert.Null(await service.BlocklistReleaseAsync(failed, "stalled"));
        }

        [Fact]
        public async Task BlocklistReleaseAsync_RecordsTheRealFailureTally()
        {
            // The release may already have failed on its own before the external caller stepped in.
            var failed = CreateFailedDownload();
            var service = CreateService([], [CreateFailedDownload(id: "download-1"), failed]);

            var entry = await service.BlocklistReleaseAsync(failed, "stalled");

            Assert.Equal(2, entry!.FailureCount);
        }

        private static BlocklistService CreateService(
            List<BlocklistEntry> blocklist,
            List<Download> downloads)
        {
            var blocklistRepository = new Mock<IBlocklistRepository>();
            blocklistRepository
                .Setup(repo => repo.GetByAudiobookIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(blocklist);
            blocklistRepository
                .Setup(repo => repo.AddAsync(It.IsAny<BlocklistEntry>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((BlocklistEntry entry, CancellationToken _) => entry);

            var downloadRepository = new Mock<IDownloadRepository>();
            downloadRepository
                .Setup(repo => repo.GetByAudiobookIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(downloads);

            return new BlocklistService(
                blocklistRepository.Object, downloadRepository.Object, NullLogger<BlocklistService>.Instance);
        }

        private static Download CreateFailedDownload(string id = "download-2", string title = ReleaseTitle) => new()
        {
            Id = id,
            AudiobookId = AudiobookId,
            Title = title,
            ReleaseGuid = "release-guid",
            IndexerId = 3,
            TotalSize = 500_000_000,
            Status = DownloadStatus.Failed,
            ErrorMessage = "torrent stalled"
        };
    }
}
