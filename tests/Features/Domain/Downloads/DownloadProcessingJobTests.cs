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
    }
}
