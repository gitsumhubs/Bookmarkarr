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

using System.Globalization;

namespace Bookmarkarr.Tests.Features.Domain.Downloads
{
    /// <summary>
    /// "Stalled" is derived from the client's label plus a duration rather than stored as a status,
    /// because the label on its own flickers on and off during a perfectly healthy transfer. These
    /// cover the derivation both sides of that debounce.
    /// </summary>
    [Trait("Name", "DownloadStallStateTests")]
    [Trait("Category", "Downloads")]
    public class DownloadStallStateTests
    {
        private static readonly DateTime Now = new(2026, 8, 17, 12, 0, 0, DateTimeKind.Utc);

        [Theory]
        [Trait("Scenario", "Only a stalled download reads as stalled")]
        [InlineData("stalledDL", true)]
        [InlineData("stalleddl", true)]
        [InlineData("  StalledDL  ", true)]
        [InlineData("stalled", true)]
        // A finished torrent nobody is asking for is seeding, not faulted. Reading it as a stall
        // would abandon downloads that have already delivered every byte.
        [InlineData("stalledUP", false)]
        [InlineData("downloading", false)]
        [InlineData("queuedDL", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsClientReportingStall_RecognisesTheDownloadStallOnly(string? clientState, bool expected)
        {
            Assert.Equal(expected, DownloadStallState.IsClientReportingStall(clientState));
        }

        [Fact]
        [Trait("Scenario", "A stall the client has only just reported is not reported onward yet")]
        public void IsStalled_BeforeTheThreshold_IsFalse()
        {
            var download = BuildDownload("stalledDL", unchangedFor: DownloadStallState.ReportingThreshold - TimeSpan.FromMinutes(1));

            Assert.False(DownloadStallState.IsStalled(download, Now, DownloadStallState.ReportingThreshold));
        }

        [Fact]
        [Trait("Scenario", "A stall that has held past the threshold is reported")]
        public void IsStalled_PastTheThreshold_IsTrue()
        {
            var download = BuildDownload("stalledDL", unchangedFor: DownloadStallState.ReportingThreshold + TimeSpan.FromMinutes(1));

            Assert.True(DownloadStallState.IsStalled(download, Now, DownloadStallState.ReportingThreshold));
        }

        [Fact]
        [Trait("Scenario", "A download nobody has timed yet is not assumed to be stalled")]
        public void IsStalled_WithoutAClock_IsFalse()
        {
            var download = new Download { Id = "d1", Status = DownloadStatus.Downloading };
            download.SetMetadata(DownloadStallState.ClientStateKey, "stalledDL");

            Assert.False(DownloadStallState.IsStalled(download, Now, DownloadStallState.ReportingThreshold));
        }

        [Fact]
        [Trait("Scenario", "A download the client calls healthy is never stalled, however long it sits")]
        public void IsStalled_WhenTheClientDoesNotSaySo_IsFalse()
        {
            var download = BuildDownload("downloading", unchangedFor: TimeSpan.FromDays(3));

            Assert.False(DownloadStallState.IsStalled(download, Now, DownloadStallState.ReportingThreshold));
        }

        [Theory]
        [Trait("Scenario", "Deliberately frozen states are not stalls")]
        [InlineData(DownloadStatus.Completed)]
        [InlineData(DownloadStatus.Moved)]
        [InlineData(DownloadStatus.Failed)]
        [InlineData(DownloadStatus.Processing)]
        [InlineData(DownloadStatus.ImportPending)]
        [InlineData(DownloadStatus.ImportBlocked)]
        [InlineData(DownloadStatus.ImportNameMismatch)]
        [InlineData(DownloadStatus.SourceMissing)]
        public void IsStalled_ForStatesOwnedByOtherWorkers_IsFalse(DownloadStatus status)
        {
            var download = BuildDownload("stalledDL", unchangedFor: TimeSpan.FromDays(3), status);

            Assert.False(DownloadStallState.CanStall(status));
            Assert.False(DownloadStallState.IsStalled(download, Now, DownloadStallState.ReportingThreshold));
        }

        [Fact]
        [Trait("Scenario", "A clock running ahead of the reading does not produce a negative age")]
        public void UnchangedFor_WithAFutureTimestamp_ClampsToZero()
        {
            var download = BuildDownload("stalledDL", unchangedFor: TimeSpan.FromHours(-2));

            Assert.Equal(TimeSpan.Zero, DownloadStallState.UnchangedFor(download, Now));
        }

        private static Download BuildDownload(
            string? clientState,
            TimeSpan unchangedFor,
            DownloadStatus status = DownloadStatus.Downloading)
        {
            var download = new Download { Id = "d1", Title = "A Book", Status = status };

            if (clientState != null)
            {
                download.SetMetadata(DownloadStallState.ClientStateKey, clientState);
            }

            download.SetMetadata(
                DownloadStallState.StateChangedAtKey,
                (Now - unchangedFor).ToString("O", CultureInfo.InvariantCulture));

            return download;
        }
    }
}
