using Bookmarkarr.Tests.Builders;
using Bookmarkarr.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Bookmarkarr.Tests.Features.Infrastructure.Repositories
{
    public class DownloadProcessingJobRepositoryTests : BaseTests
    {
        [Fact]
        public async Task RetryCount_ShouldPersist_AfterDbContextRecreation()
        {
            using var connection = new SqliteConnection("Filename=:memory:");
            connection.Open();

            var options = new DbContextOptionsBuilder<BookmarkarrDbContext>()
                .UseSqlite(connection)
                .Options;

            using (var context = new BookmarkarrDbContext(options))
            {
                context.Database.EnsureCreated();
                var job = new DownloadProcessingJobBuilder()
                    .WithPending(at: DateTime.UtcNow)
                    .Build();

                context.DownloadProcessingJobs.Add(job);
                await context.SaveChangesAsync();

                job.RetryCount = 4;
                await context.SaveChangesAsync();
            }

            using (var context = new BookmarkarrDbContext(options))
            {
                var job = await context.DownloadProcessingJobs.FirstOrDefaultAsync();

                Assert.NotNull(job);
                Assert.Equal(4, job.RetryCount);
            }
        }
    }
}
