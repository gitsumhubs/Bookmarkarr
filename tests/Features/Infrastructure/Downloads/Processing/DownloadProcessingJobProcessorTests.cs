using Bookmarkarr.Tests.Builders;
using Bookmarkarr.Tests.Common;
using Bookmarkarr.Tests.Mocks;
using Bookmarkarr.Domain.Books;
using Microsoft.EntityFrameworkCore;

namespace Bookmarkarr.Tests.Features.Infrastructure.Downloads.Processing
{
    [Trait("Name", "DownloadProcessingJobProcessorTests")]
    [Trait("Category", "DownloadProcessingJob")]
    public class DownloadProcessingJobProcessorTests : BaseTests
    {
        private readonly Mock<IDownloadImportService> downloadImportServiceMock = new();
        private readonly DownloadClientGatewayMock downloadClientGatewayMock = new();

        public override async Task InitializeAsync()
        {
            _services.AddSingleton<IDownloadClientGateway>(downloadClientGatewayMock);
            Init();
        }

        [Fact]
        public async Task CompletedDownload_With_NoPathFails()
        {
            var download = await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithAudiobook(await CreateAudiobook())
                .WithDownloadClientConfiguration(await CreateDownloadClientConfiguration())
                .WithPath("")
                .WithCompletedStatus(at: DateTime.UtcNow)
                .Build());

            var job = await _downloadProcessingJobRepository.AddAsync(new DownloadProcessingJobBuilder()
                .WithDownload(download)
                .Build());

            var downloadProcessingJobProcessor = _provider.GetRequiredService<DownloadProcessingJobProcessor>();
            await downloadProcessingJobProcessor.ProcessQueueAsync(CancellationToken.None);

            downloadImportServiceMock.Verify(m => m.ImportDownloadFilesAsync(
                    It.IsAny<Audiobook>(),
                    It.IsAny<List<string>>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<DownloadImportOptions?>()),
                Times.Never);

            job = await _downloadProcessingJobRepository.GetByIdAsync(job.Id);
            Assert.NotNull(job);
            Assert.Equal(ProcessingJobStatus.Failed, job.Status);
        }

        [Theory]
        [InlineData("directoryMissing", false)]
        [InlineData("directoryExists", true)]
        public async Task CompletedDownload_With_MissingSource(string path, bool pathExists)
        {
            var sourceDirectory = FileService.GetTempDirectory("source-directory");
            path = Path.Join(sourceDirectory, path);
            if (pathExists)
            {
                Directory.CreateDirectory(path);
            }

            var download = await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithAudiobook(await CreateAudiobook())
                .WithDownloadClientConfiguration(await CreateDownloadClientConfiguration())
                .WithPath(path)
                .WithCompletedStatus(at: DateTime.UtcNow)
                .Build());

            var job = await _downloadProcessingJobRepository.AddAsync(new DownloadProcessingJobBuilder()
                .WithDownload(download)
                .Build());

            // First try
            var downloadProcessingJobProcessor = _provider.GetRequiredService<DownloadProcessingJobProcessor>();
            await downloadProcessingJobProcessor.ProcessQueueAsync(CancellationToken.None);

            downloadImportServiceMock.Verify(m => m.ImportDownloadFilesAsync(
                    It.IsAny<Audiobook>(),
                    It.IsAny<List<string>>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<DownloadImportOptions?>()),
                Times.Never);

            job = await _downloadProcessingJobRepository.GetByIdAsync(job.Id);
            Assert.NotNull(job);
            Assert.Equal(ProcessingJobStatus.Pending, job.Status);
            Assert.Equal(1, job.RetryCount);
            Assert.NotEmpty(job.ErrorMessage);

            download = await _downloadRepository.FindAsync(download.Id);
            Assert.NotNull(download);
            Assert.Equal(DownloadStatus.ImportPending, download.Status);

            job.RetryCount = job.MaxRetries;
            await TestUtils.CancelJobRetryWait(_downloadProcessingJobRepository, job);

            // Last try
            await downloadProcessingJobProcessor.ProcessQueueAsync(CancellationToken.None);

            job = await _downloadProcessingJobRepository.GetByIdAsync(job.Id);
            Assert.NotNull(job);
            Assert.Equal(ProcessingJobStatus.Failed, job.Status);

            download = await _downloadRepository.FindAsync(download.Id);
            Assert.NotNull(download);
            Assert.Equal(DownloadStatus.ImportBlocked, download.Status);
            Assert.NotNull(download.ImportBlockMessages);
            Assert.Contains(download.ImportBlockMessages, m => m.Contains($"job {job.Id}", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(download.ImportBlockMessages, m => m.Contains("{job.Id}", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        [Trait("Scenario", "ExternalImportResolverRecoversStaleDownloadPath")]
        public async Task Import_ExternalClientStaleDownloadPath_UsesResolvedSourceFiles()
        {
            // Arrange
            var source = FileService.GetTempDirectory("source");
            var filePath = await FileService.GetFileAsync(source, "audiobook.mp3");
            var stalePath = Path.Join(FileService.GetTempDirectory("stale-source"), "missing-client-path");

            downloadClientGatewayMock.SourceFiles = [filePath];

            var download = await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithCompletedStatus(at: DateTime.UtcNow)
                .WithDownloadClientConfiguration(await CreateDownloadClientConfiguration())
                .WithAudiobook(await CreateAudiobook())
                .WithPath(stalePath)
                .Build());

            await _downloadProcessingJobRepository.AddAsync(new DownloadProcessingJobBuilder()
                .WithDownload(download)
                .Build());

            // Act
            var processor = _provider.GetRequiredService<DownloadProcessingJobProcessor>();
            await processor.ProcessQueueAsync(CancellationToken.None);

            // Assert
            download = await _downloadRepository.GetByIdAsync(download.Id);
            Assert.NotNull(download);
            Assert.Equal(DownloadStatus.Moved, download.Status);
            Assert.Equal(1, downloadClientGatewayMock.GetCallCount(nameof(downloadClientGatewayMock.GetQueueItemAsync)));
        }

        [Fact]
        [Trait("Scenario", "DirectDownloadMissingStagedFileRetries")]
        public async Task Import_DirectDownloadMissingStagedFile_RetriesWithoutExternalClientRecovery()
        {
            // Arrange
            var missingPath = Path.Join(FileService.GetTempDirectory("ddl-source"), "missing.m4b");
            var audiobook = await CreateAudiobook();
            var download = await _downloadRepository.AddAsync(new Download
            {
                Id = $"ddl-{Guid.NewGuid():N}",
                AudiobookId = audiobook.Id,
                Title = "DDL Book",
                Artist = "DDL Author",
                Album = "DDL Book",
                DownloadClientId = DirectDownloadMetadataKeys.ClientId,
                Status = DownloadStatus.Completed,
                StartedAt = DateTime.UtcNow.AddMinutes(-5),
                CompletedAt = DateTime.UtcNow,
                DownloadPath = missingPath,
                Metadata = new Dictionary<string, object>
                {
                    [DirectDownloadMetadataKeys.DownloadType] = DirectDownloadMetadataKeys.ClientId
                }
            });

            var job = await _downloadProcessingJobRepository.AddAsync(new DownloadProcessingJobBuilder()
                .WithDownload(download)
                .Build());

            // Act
            var processor = _provider.GetRequiredService<DownloadProcessingJobProcessor>();
            await processor.ProcessQueueAsync(CancellationToken.None);

            // Assert
            job = await _downloadProcessingJobRepository.GetByIdAsync(job.Id);
            Assert.NotNull(job);
            Assert.Equal(ProcessingJobStatus.Pending, job.Status);
            Assert.Equal(1, job.RetryCount);
            Assert.Contains("Direct-download source path not found", job.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, downloadClientGatewayMock.GetCallCount(nameof(downloadClientGatewayMock.GetQueueItemAsync)));
        }

        [Fact]
        public async Task Import_DirectDownloadArchivePlan_ForcesArchiveExtraction()
        {
            // Given
            var importService = new Mock<IDownloadImportService>();
            importService
                .Setup(service => service.ImportDownloadFilesAsync(
                    It.IsAny<Audiobook>(),
                    It.IsAny<List<string>>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<DownloadImportOptions?>()))
                .ReturnsAsync((Audiobook _, List<string> files, CancellationToken _, DownloadImportOptions? _) =>
                    [ImportResult.ImportSuccess(FileAction.Copy, files[0], files[0], wasRegisteredToAudiobook: true)]);
            Init(builder => builder.WithSingleton<IDownloadImportService>(importService.Object));
            var sourceDirectory = FileService.GetTempDirectory("ddl-archive-source");
            var archivePath = await FileService.GetFileAsync(sourceDirectory, "book.zip");
            var audiobook = await CreateAudiobook();
            var download = await _downloadRepository.AddAsync(new Download
            {
                Id = $"ddl-{Guid.NewGuid():N}",
                AudiobookId = audiobook.Id,
                Title = "DDL Archive Book",
                Artist = "DDL Author",
                Album = "DDL Archive Book",
                DownloadClientId = DirectDownloadMetadataKeys.ClientId,
                Status = DownloadStatus.Completed,
                StartedAt = DateTime.UtcNow.AddMinutes(-5),
                CompletedAt = DateTime.UtcNow,
                DownloadPath = archivePath,
                Metadata = new Dictionary<string, object>
                {
                    [DirectDownloadMetadataKeys.DownloadType] = DirectDownloadMetadataKeys.ClientId,
                    [DirectDownloadMetadataKeys.RequiresArchiveExtraction] = true
                }
            });
            await _downloadProcessingJobRepository.AddAsync(new DownloadProcessingJobBuilder()
                .WithDownload(download)
                .Build());

            // When
            await _provider.GetRequiredService<DownloadProcessingJobProcessor>()
                .ProcessQueueAsync(CancellationToken.None);

            // Then
            importService.Verify(service => service.ImportDownloadFilesAsync(
                It.Is<Audiobook>(item => item.Id == audiobook.Id),
                It.Is<List<string>>(files => files.Contains(archivePath)),
                It.IsAny<CancellationToken>(),
                It.Is<DownloadImportOptions>(options => options.ForceArchiveExtraction)), Times.Once);
        }

        [Fact]
        public async Task Import_SingleFile_UpdatesStatus()
        {
            // Arrange
            var source = FileService.GetTempDirectory("source");
            var filePath = await FileService.GetFileAsync(source, "audiobook.mp3");

            downloadClientGatewayMock.SourceFiles = [filePath];

            var download = await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithCompletedStatus(at: DateTime.UtcNow)
                .WithDownloadClientConfiguration(await CreateDownloadClientConfiguration())
                .WithAudiobook(await CreateAudiobook())
                .WithPath(source)
                .Build());

            await _downloadProcessingJobRepository.AddAsync(new DownloadProcessingJobBuilder()
                .WithDownload(download)
                .Build());

            // Act
            var processor = _provider.GetRequiredService<DownloadProcessingJobProcessor>();
            await processor.ProcessQueueAsync(CancellationToken.None);

            // Assert
            download = await _downloadRepository.GetByIdAsync(download.Id);
            Assert.NotNull(download);
            Assert.True(download.Status == DownloadStatus.Moved, $"Expected Moved, got {download.Status}");
        }

        [Fact]
        [Trait("Scenario", "ImportSuccessEnqueuesLibraryScan")]
        public async Task Import_Success_EnqueuesScanForAudiobookLibraryPath()
        {
            // Arrange
            var source = FileService.GetTempDirectory("source");
            var filePath = await FileService.GetFileAsync(source, "audiobook.mp3");

            downloadClientGatewayMock.SourceFiles = [filePath];

            var audiobook = await CreateAudiobook();
            var download = await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithCompletedStatus(at: DateTime.UtcNow)
                .WithDownloadClientConfiguration(await CreateDownloadClientConfiguration())
                .WithAudiobook(audiobook)
                .WithPath(source)
                .Build());

            await _downloadProcessingJobRepository.AddAsync(new DownloadProcessingJobBuilder()
                .WithDownload(download)
                .Build());

            // Act
            var processor = _provider.GetRequiredService<DownloadProcessingJobProcessor>();
            await processor.ProcessQueueAsync(CancellationToken.None);

            // Assert
            var scanQueue = Assert.IsType<ScanQueueService>(_provider.GetRequiredService<IScanQueueService>());
            Assert.True(scanQueue.Reader.TryRead(out var scanJob));
            Assert.Equal(audiobook.Id, scanJob.AudiobookId);
            Assert.Null(scanJob.Path);
            Assert.Equal(download.Id, scanJob.DownloadId);
        }

        [Fact]
        [Trait("Scenario", "StaleMovedImportJobIsIdempotent")]
        public async Task ProcessQueue_AlreadyMovedDownload_CompletesJobWithoutImportOrScan()
        {
            // Arrange
            var source = FileService.GetTempDirectory("source");
            await FileService.GetFileAsync(source, "audiobook.mp3");

            var download = await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithStatus(DownloadStatus.Moved)
                .WithDownloadClientConfiguration(await CreateDownloadClientConfiguration())
                .WithAudiobook(await CreateAudiobook())
                .WithPath(source)
                .Build());

            var job = await _downloadProcessingJobRepository.AddAsync(new DownloadProcessingJobBuilder()
                .WithDownload(download)
                .Build());

            // Act
            var processor = _provider.GetRequiredService<DownloadProcessingJobProcessor>();
            await processor.ProcessQueueAsync(CancellationToken.None);

            // Assert
            job = await _downloadProcessingJobRepository.GetByIdAsync(job.Id);
            Assert.NotNull(job);
            Assert.Equal(ProcessingJobStatus.Completed, job.Status);
            Assert.Contains(job.ProcessingLog, m => m.Contains("already imported", StringComparison.OrdinalIgnoreCase));

            download = await _downloadRepository.GetByIdAsync(download.Id);
            Assert.NotNull(download);
            Assert.Equal(DownloadStatus.Moved, download.Status);

            downloadImportServiceMock.Verify(m => m.ImportDownloadFilesAsync(
                    It.IsAny<Audiobook>(),
                    It.IsAny<List<string>>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<DownloadImportOptions?>()),
                Times.Never);

            Assert.Equal(0, downloadClientGatewayMock.GetCallCount(nameof(downloadClientGatewayMock.GetQueueItemAsync)));
            var scanQueue = Assert.IsType<ScanQueueService>(_provider.GetRequiredService<IScanQueueService>());
            Assert.False(scanQueue.Reader.TryRead(out _));
        }

        [Fact]
        public async Task Import_MultipleFiles_UpdatesStatus()
        {
            // Arrange
            var source = FileService.GetTempDirectory("source");
            var filePath1 = await FileService.GetFileAsync(source, "audiobook1.mp3");
            var filePath2 = await FileService.GetFileAsync(source, "audiobook2.mp3");
            var filePath3 = await FileService.GetFileAsync(source, "audiobook3.mp3");

            downloadClientGatewayMock.SourceFiles = [
                filePath1,
                filePath2,
                filePath3
            ];

            var download = await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithCompletedStatus(at: DateTime.UtcNow)
                .WithDownloadClientConfiguration(await CreateDownloadClientConfiguration())
                .WithAudiobook(await CreateAudiobook())
                .WithPath(source)
                .Build());

            await _downloadProcessingJobRepository.AddAsync(new DownloadProcessingJobBuilder()
                .WithDownload(download)
                .Build());

            // Act
            var processor = _provider.GetRequiredService<DownloadProcessingJobProcessor>();
            await processor.ProcessQueueAsync(CancellationToken.None);

            // Assert
            download = await _downloadRepository.GetByIdAsync(download.Id);
            Assert.NotNull(download);
            Assert.True(download.Status == DownloadStatus.Moved, $"Expected Moved, got {download.Status}");
        }

        [Fact]
        [Trait("Scenario", "DebridClientOmitsCompanionFiles")]
        public async Task Import_MissingNonAudioCompanions_ImportsAvailableAudio()
        {
            var basePath = FileService.GetTempDirectory("destination");
            var sourcePath = FileService.GetTempDirectory("downloads");
            var audioPath = await FileService.GetFileAsync(sourcePath, "Target Book.m4b");
            var missingCoverPath = Path.Join(sourcePath, "Target Book.jpg");
            var missingCuePath = Path.Join(sourcePath, "Target Book.cue");

            downloadClientGatewayMock.SourceFiles = [audioPath, missingCoverPath, missingCuePath];

            var audiobook = await _audiobookRepository.AddAsync(new AudiobookBuilder()
                .WithBasePath(basePath)
                .Build());
            var download = await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithAudiobook(audiobook)
                .WithDownloadClientConfiguration(await CreateDownloadClientConfiguration())
                .WithCompletedStatus(at: DateTime.UtcNow)
                .WithPath(sourcePath)
                .Build());
            var job = await _downloadProcessingJobRepository.AddAsync(new DownloadProcessingJobBuilder()
                .WithDownload(download)
                .Build());

            var processor = _provider.GetRequiredService<DownloadProcessingJobProcessor>();
            await processor.ProcessQueueAsync(CancellationToken.None);

            download = await _downloadRepository.GetByIdAsync(download.Id);
            job = await _downloadProcessingJobRepository.GetByIdAsync(job.Id);
            Assert.Equal(DownloadStatus.Moved, download!.Status);
            Assert.Equal(ProcessingJobStatus.Completed, job!.Status);
            Assert.Contains(job.ProcessingLog, entry => entry.Contains("missing non-audio companion", StringComparison.OrdinalIgnoreCase));
            Assert.True(File.Exists(Path.Join(basePath, "Target Book.m4b")));
        }

        [Fact]
        [Trait("Scenario", "DebridClientOmitsEbookCompanionFiles")]
        public async Task Import_MissingNonEbookCompanions_ImportsAvailableEbook()
        {
            var ebookRoot = FileService.GetTempDirectory("ebook-destination");
            var sourcePath = FileService.GetTempDirectory("ebook-downloads");
            var ebookPath = await FileService.GetFileAsync(sourcePath, "Target Book.epub");
            var missingCoverPath = Path.Join(sourcePath, "Target Book.jpg");
            var missingMetadataPath = Path.Join(sourcePath, "metadata.opf");
            downloadClientGatewayMock.SourceFiles = [ebookPath, missingCoverPath, missingMetadataPath];

            var book = new AudiobookBuilder()
                .WithTitle("Target Book")
                .WithAuthor("Target Author")
                .WithBasePath(FileService.GetTempDirectory("audio-destination"))
                .Build();
            var ebookEdition = BookEdition.Create(book.Id, EditionMediaType.Ebook);
            ebookEdition.RootPath = ebookRoot;
            book.Editions.Add(ebookEdition);
            book = await _audiobookRepository.AddAsync(book);

            var download = new DownloadBuilder()
                .WithAudiobook(book)
                .WithDownloadClientConfiguration(await CreateDownloadClientConfiguration())
                .WithCompletedStatus(at: DateTime.UtcNow)
                .WithPath(sourcePath)
                .Build();
            download.EditionId = ebookEdition.Id;
            download.Metadata["ContentType"] = DownloadContentTypes.Ebook;
            download = await _downloadRepository.AddAsync(download);
            var job = await _downloadProcessingJobRepository.AddAsync(new DownloadProcessingJobBuilder()
                .WithDownload(download)
                .Build());

            var previousEbookRoot = Environment.GetEnvironmentVariable("BOOKMARKARR_EBOOK_ROOT");
            Environment.SetEnvironmentVariable("BOOKMARKARR_EBOOK_ROOT", ebookRoot);
            try
            {
                var processor = _provider.GetRequiredService<DownloadProcessingJobProcessor>();
                await processor.ProcessQueueAsync(CancellationToken.None);
            }
            finally
            {
                Environment.SetEnvironmentVariable("BOOKMARKARR_EBOOK_ROOT", previousEbookRoot);
            }

            download = await _downloadRepository.GetByIdAsync(download.Id);
            job = await _downloadProcessingJobRepository.GetByIdAsync(job.Id);
            Assert.Equal(DownloadStatus.Moved, download!.Status);
            Assert.Equal(ProcessingJobStatus.Completed, job!.Status);
            Assert.Contains(job.ProcessingLog, entry => entry.Contains("missing non-ebook companion", StringComparison.OrdinalIgnoreCase));
            Assert.Single(Directory.EnumerateFiles(ebookRoot, "*.epub", SearchOption.AllDirectories));
            Assert.Empty(Directory.EnumerateFiles(ebookRoot, "*.m4b", SearchOption.AllDirectories));
        }

        [Fact]
        [Trait("Scenario", "MixedBundleRoutesFilesToMatchingEditions")]
        public async Task Import_MixedBundle_RegistersAudioAndEbookOnlyToMatchingEditions()
        {
            var audioRoot = FileService.GetTempDirectory("mixed-audio-library");
            var ebookRoot = FileService.GetTempDirectory("mixed-ebook-library");
            var sourcePath = FileService.GetTempDirectory("mixed-download");
            var audioPath = await FileService.GetFileAsync(sourcePath, "Target Book.m4b");
            var ebookPath = await FileService.GetFileAsync(sourcePath, "Target Book.epub");
            downloadClientGatewayMock.SourceFiles = [audioPath, ebookPath];

            var book = new AudiobookBuilder()
                .WithTitle("Target Book")
                .WithAuthor("Target Author")
                .WithBasePath(audioRoot)
                .Build();
            var audioEdition = BookEdition.Create(book.Id, EditionMediaType.Audiobook);
            audioEdition.RootPath = audioRoot;
            var ebookEdition = BookEdition.Create(book.Id, EditionMediaType.Ebook);
            ebookEdition.RootPath = ebookRoot;
            book.Editions.AddRange([audioEdition, ebookEdition]);
            book = await _audiobookRepository.AddAsync(book);

            var download = new DownloadBuilder()
                .WithAudiobook(book)
                .WithDownloadClientConfiguration(await CreateDownloadClientConfiguration())
                .WithCompletedStatus(at: DateTime.UtcNow)
                .WithPath(sourcePath)
                .Build();
            download.EditionId = audioEdition.Id;
            download.Metadata["ContentType"] = DownloadContentTypes.Audiobook;
            download = await _downloadRepository.AddAsync(download);
            await _downloadProcessingJobRepository.AddAsync(new DownloadProcessingJobBuilder()
                .WithDownload(download)
                .Build());

            var previousEbookRoot = Environment.GetEnvironmentVariable("BOOKMARKARR_EBOOK_ROOT");
            Environment.SetEnvironmentVariable("BOOKMARKARR_EBOOK_ROOT", ebookRoot);
            try
            {
                await _provider.GetRequiredService<DownloadProcessingJobProcessor>()
                    .ProcessQueueAsync(CancellationToken.None);
            }
            finally
            {
                Environment.SetEnvironmentVariable("BOOKMARKARR_EBOOK_ROOT", previousEbookRoot);
            }

            var db = _provider.GetRequiredService<BookmarkarrDbContext>();
            var editions = await db.BookEditions
                .Include(edition => edition.Files)
                .Where(edition => edition.BookId == book.Id)
                .ToListAsync();
            var persistedAudio = Assert.Single(editions, edition => edition.MediaType == EditionMediaType.Audiobook);
            var persistedEbook = Assert.Single(editions, edition => edition.MediaType == EditionMediaType.Ebook);
            var registeredAudio = Assert.Single(persistedAudio.Files);
            var registeredEbook = Assert.Single(persistedEbook.Files);

            Assert.EndsWith(".m4b", registeredAudio.Path, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(FileUtils.NormalizeStoredPath(audioRoot), registeredAudio.Path, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith(".epub", registeredEbook.Path, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(FileUtils.NormalizeStoredPath(ebookRoot), registeredEbook.Path, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(persistedAudio.Files, file => FileUtils.IsEbookFile(file.Path));
            Assert.DoesNotContain(persistedEbook.Files, file => FileUtils.IsAudioFile(file.Path));
        }

        [Fact]
        [Trait("Scenario", "DebridClientOmitsAudioFile")]
        public async Task Import_MissingAudioFile_Retries()
        {
            var sourcePath = FileService.GetTempDirectory("downloads");
            var coverPath = await FileService.GetFileAsync(sourcePath, "Target Book.jpg");
            var missingAudioPath = Path.Join(sourcePath, "Target Book.m4b");
            downloadClientGatewayMock.SourceFiles = [missingAudioPath, coverPath];

            var download = await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithAudiobook(await CreateAudiobook())
                .WithDownloadClientConfiguration(await CreateDownloadClientConfiguration())
                .WithCompletedStatus(at: DateTime.UtcNow)
                .WithPath(sourcePath)
                .Build());
            var job = await _downloadProcessingJobRepository.AddAsync(new DownloadProcessingJobBuilder()
                .WithDownload(download)
                .Build());

            var processor = _provider.GetRequiredService<DownloadProcessingJobProcessor>();
            await processor.ProcessQueueAsync(CancellationToken.None);

            download = await _downloadRepository.GetByIdAsync(download.Id);
            job = await _downloadProcessingJobRepository.GetByIdAsync(job.Id);
            Assert.Equal(DownloadStatus.ImportPending, download!.Status);
            Assert.Equal(ProcessingJobStatus.Pending, job!.Status);
            Assert.Equal(1, job.RetryCount);
            Assert.Contains("Audio or archive files", job.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Import_OnlyRelevantFiles()
        {
            var basePath = FileService.GetTempDirectory("destination");
            var sourcePath = FileService.GetTempDirectory("downloads");
            var targetAudioPath = await FileService.GetFileAsync(sourcePath, "Target Book.m4b");
            var coverPath = await FileService.GetFileAsync(sourcePath, "cover.jpg");
            await FileService.GetFileAsync(sourcePath, "Different Book.m4b");

            downloadClientGatewayMock.SourceFiles = [
                targetAudioPath,
                coverPath
            ];

            var audiobook = await _audiobookRepository.AddAsync(new AudiobookBuilder()
                .WithBasePath(basePath)
                .Build());

            var download = await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithAudiobook(audiobook)
                .WithDownloadClientConfiguration(await CreateDownloadClientConfiguration())
                .WithCompletedStatus(at: DateTime.UtcNow)
                .WithPath(sourcePath)
                .Build());

            await _downloadProcessingJobRepository.AddAsync(new DownloadProcessingJobBuilder()
                .WithDownload(download)
                .Build());

            // Act
            var processor = _provider.GetRequiredService<DownloadProcessingJobProcessor>();
            await processor.ProcessQueueAsync(CancellationToken.None);

            Assert.True(File.Exists(Path.Join(basePath, "Target Book.m4b")));
            Assert.True(File.Exists(Path.Join(basePath, "cover.jpg")));
            Assert.False(File.Exists(Path.Join(basePath, "Different Book.m4b")));
        }

        [Fact]
        public async Task RetryJob_IsNotProcessedBeforeTheRetryTimerExpires()
        {
            var sourceDirectory = FileService.GetTempDirectory("source-directory");
            var path = Path.Join(sourceDirectory, "missing");

            var download = await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithAudiobook(await CreateAudiobook())
                .WithDownloadClientConfiguration(await CreateDownloadClientConfiguration())
                .WithPath(path)
                .WithCompletedStatus(at: DateTime.UtcNow)
                .Build());

            var job = await _downloadProcessingJobRepository.AddAsync(new DownloadProcessingJobBuilder()
                .WithDownload(download)
                .Build());

            // First try: Should trigger a retry
            var downloadProcessingJobProcessor = _provider.GetRequiredService<DownloadProcessingJobProcessor>();
            await downloadProcessingJobProcessor.ProcessQueueAsync(CancellationToken.None);

            downloadImportServiceMock.Verify(m => m.ImportDownloadFilesAsync(
                    It.IsAny<Audiobook>(),
                    It.IsAny<List<string>>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<DownloadImportOptions?>()),
                Times.Never);

            job = await _downloadProcessingJobRepository.GetByIdAsync(job.Id);
            Assert.NotNull(job);
            Assert.Equal(ProcessingJobStatus.Pending, job.Status);
            Assert.Equal(1, job.RetryCount);
            Assert.NotEmpty(job.ErrorMessage);

            download = await _downloadRepository.FindAsync(download.Id);
            Assert.NotNull(download);
            Assert.Equal(DownloadStatus.ImportPending, download.Status);

            // Retry immediately
            await downloadProcessingJobProcessor.ProcessQueueAsync(CancellationToken.None);

            // Job is not modified
            job = await _downloadProcessingJobRepository.GetByIdAsync(job.Id);
            Assert.NotNull(job);
            Assert.Equal(ProcessingJobStatus.Pending, job.Status);
            Assert.Equal(1, job.RetryCount);

            // Retry after timer expires
            await TestUtils.CancelJobRetryWait(_downloadProcessingJobRepository, job);
            await downloadProcessingJobProcessor.ProcessQueueAsync(CancellationToken.None);

            // Job is retried (and refailed)
            job = await _downloadProcessingJobRepository.GetByIdAsync(job.Id);
            Assert.NotNull(job);
            Assert.Equal(ProcessingJobStatus.Pending, job.Status);
            Assert.Equal(2, job.RetryCount);
        }

        [Fact]
        public async Task ProcessJob_MarkItemImported()
        {
            var sourceDirectory = FileService.GetTempDirectory("source-directory");
            var file1 = await FileService.GetFileAsync(sourceDirectory, "Target Book.m4b");

            downloadClientGatewayMock.SourceFiles = [file1];

            var download = await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithAudiobook(await CreateAudiobook())
                .WithDownloadClientConfiguration(await CreateDownloadClientConfiguration())
                .WithPath(sourceDirectory)
                .WithCompletedStatus(at: DateTime.UtcNow)
                .Build());

            var job = await _downloadProcessingJobRepository.AddAsync(new DownloadProcessingJobBuilder()
                .WithDownload(download)
                .Build());

            Assert.Equal(0, downloadClientGatewayMock.GetCallCount(nameof(downloadClientGatewayMock.MarkItemAsImportedAsync)));

            // Process the job
            var downloadProcessingJobProcessor = _provider.GetRequiredService<DownloadProcessingJobProcessor>();
            await downloadProcessingJobProcessor.ProcessQueueAsync(CancellationToken.None);

            // Job is retried (and refailed)
            job = await _downloadProcessingJobRepository.GetByIdAsync(job.Id);
            Assert.NotNull(job);
            Assert.Equal(ProcessingJobStatus.Completed, job.Status);

            Assert.Equal(1, downloadClientGatewayMock.GetCallCount(nameof(downloadClientGatewayMock.MarkItemAsImportedAsync)));
        }

        [Fact]
        public async Task FinalizationRetry_DoesNotRepeatCompletedFileImport()
        {
            var sourceDirectory = FileService.GetTempDirectory("checkpoint-source");
            var file = await FileService.GetFileAsync(sourceDirectory, "Checkpoint Book.m4b");
            downloadClientGatewayMock.SourceFiles = [file];
            downloadClientGatewayMock.MarkImportedResult = false;

            var download = await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithAudiobook(await CreateAudiobook())
                .WithDownloadClientConfiguration(await CreateDownloadClientConfiguration())
                .WithPath(sourceDirectory)
                .WithCompletedStatus(DateTime.UtcNow)
                .Build());
            var job = await _downloadProcessingJobRepository.AddAsync(new DownloadProcessingJobBuilder()
                .WithDownload(download)
                .Build());

            var processor = _provider.GetRequiredService<DownloadProcessingJobProcessor>();
            await processor.ProcessQueueAsync(CancellationToken.None);

            job = (await _downloadProcessingJobRepository.GetByIdAsync(job.Id))!;
            Assert.True(job.HasCheckpoint("FilesImported"));
            Assert.False(job.HasCheckpoint("ClientMarkedImported"));
            Assert.Equal(DownloadStatus.ImportPending, (await _downloadRepository.GetByIdAsync(download.Id))!.Status);
            Assert.Equal(1, downloadClientGatewayMock.GetCallCount(nameof(downloadClientGatewayMock.GetQueueItemAsync)));

            downloadClientGatewayMock.MarkImportedResult = true;
            await TestUtils.CancelJobRetryWait(_downloadProcessingJobRepository, job);
            await processor.ProcessQueueAsync(CancellationToken.None);

            Assert.Equal(DownloadStatus.Moved, (await _downloadRepository.GetByIdAsync(download.Id))!.Status);
            Assert.Equal(1, downloadClientGatewayMock.GetCallCount(nameof(downloadClientGatewayMock.GetQueueItemAsync)));
            Assert.Equal(2, downloadClientGatewayMock.GetCallCount(nameof(downloadClientGatewayMock.MarkItemAsImportedAsync)));
        }
    }
}
