namespace Bookmarkarr.Tests.Features.Infrastructure.Downloads.Processing
{
    [Trait("Name", "DownloadProcessingSourceDiagnosticsTests")]
    [Trait("Category", "DownloadProcessingJob")]
    public class DownloadProcessingSourceDiagnosticsTests
    {
        [Fact]
        [Trait("Scenario", "An unreachable directory names remote path mapping instead of missing files")]
        public void DescribeMissingSourceFiles_WhenNoDirectoryResolves_PointsAtRemotePathMappings()
        {
            // Regression: a client configured to report host paths rather than its own container
            // paths hands over a directory that does not exist here, so every file reads as
            // missing. The old wording — "No importable files found" — is true but sends the
            // reader hunting for files when the fix is a path mapping.
            var reported = new[]
            {
                Path.Combine(Path.GetTempPath(), "bookmarkarr-tests", Guid.NewGuid().ToString("N"), "Book.m4b")
            };

            var reason = DownloadProcessingJobProcessor.DescribeMissingSourceFiles(reported);

            Assert.Contains("Directory not found", reason, StringComparison.Ordinal);
            Assert.Contains("Remote Path Mappings", reason, StringComparison.Ordinal);
        }

        [Fact]
        [Trait("Scenario", "A reachable directory keeps the original wording")]
        public void DescribeMissingSourceFiles_WhenDirectoryExists_KeepsTheFileWording()
        {
            // The directory resolving means paths are fine and the files themselves are the
            // problem, so the mapping advice would be a false lead.
            var directory = Path.Combine(
                Path.GetTempPath(),
                "bookmarkarr-tests",
                nameof(DownloadProcessingSourceDiagnosticsTests),
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);

            try
            {
                var reason = DownloadProcessingJobProcessor.DescribeMissingSourceFiles(
                    [Path.Combine(directory, "Book.m4b")]);

                Assert.Equal("No importable files found", reason);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        [Trait("Scenario", "An empty list is reported as the client sending nothing")]
        public void DescribeMissingSourceFiles_WhenClientReportedNothing_SaysSo()
        {
            var reason = DownloadProcessingJobProcessor.DescribeMissingSourceFiles([]);

            Assert.Equal("The download client reported no files for this download", reason);
        }

        [Fact]
        [Trait("Scenario", "A partially reachable set is not blamed on path mapping")]
        public void DescribeMissingSourceFiles_WhenOnlySomeDirectoriesResolve_KeepsTheFileWording()
        {
            var existing = Path.Combine(
                Path.GetTempPath(),
                "bookmarkarr-tests",
                nameof(DownloadProcessingSourceDiagnosticsTests),
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(existing);

            try
            {
                var reason = DownloadProcessingJobProcessor.DescribeMissingSourceFiles(
                [
                    Path.Combine(existing, "Book.m4b"),
                    Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "Other.m4b")
                ]);

                Assert.Equal("No importable files found", reason);
            }
            finally
            {
                Directory.Delete(existing, recursive: true);
            }
        }
    }
}
