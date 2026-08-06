using Bookmarkarr.Tests.Builders;
using Bookmarkarr.Tests.Common;

namespace Bookmarkarr.Tests.Features.Domain.Downloads
{
    public class DownloadProcessingJob : BaseTests
    {
        [Fact]
        public async Task ScheduleRetry_FirstAttempt_KeepsImportPendingForRetry()
        {
            var job = new DownloadProcessingJobBuilder()
                .Build();

            job.ScheduleRetry();

            Assert.Equal(1, job.RetryCount);
            Assert.Equal(ProcessingJobStatus.Pending, job.Status);

            job.RetryCount = job.MaxRetries;

            job.ScheduleRetry();

            Assert.True(job.RetryCount >= job.MaxRetries);
            Assert.Equal(ProcessingJobStatus.Failed, job.Status);
        }

        [Fact]
        public void ScheduleRetry_SpreadsAttemptsAcrossAboutAnHour()
        {
            // Regression: the old 0.5/1/2/4-minute schedule exhausted the whole budget in about
            // seven minutes, which is far less time than a large transfer needs to finish landing
            // on disk after its client reports completion.
            var job = new DownloadProcessingJobBuilder().Build();
            var start = DateTime.UtcNow;

            var delays = new List<double>();
            for (var attempt = 0; attempt < job.MaxRetries; attempt++)
            {
                job.ScheduleRetry();
                delays.Add((job.NextRetryAt!.Value - start).TotalMinutes);
            }

            // Each wait is longer than the last, and the final one is well beyond seven minutes.
            Assert.Equal(delays.OrderBy(delay => delay), delays);
            Assert.True(delays[^1] > 30, $"final backoff was only {delays[^1]:F1} minutes");
        }
    }
}
