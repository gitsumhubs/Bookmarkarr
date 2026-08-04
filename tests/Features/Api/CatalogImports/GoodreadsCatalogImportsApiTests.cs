/* Bookmarkarr is licensed under the GNU AGPL v3 or later. */
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Bookmarkarr.Domain.Books;
using Bookmarkarr.Tests.Mocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bookmarkarr.Tests.Features.Api.CatalogImports;

public sealed class GoodreadsCatalogImportsApiTests : IClassFixture<BookmarkarrWebApplicationFactory>
{
    private readonly BookmarkarrWebApplicationFactory _factory;

    public GoodreadsCatalogImportsApiTests(BookmarkarrWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task PreviewAndCommit_DefaultBothEditions_IsAdditiveIdempotentAndDoesNotSearch()
    {
        var downloadServiceMock = new Mock<IDownloadService>(MockBehavior.Strict);

        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDownloadService>();
                services.AddSingleton(downloadServiceMock.Object);
            }));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", await GetAntiforgeryTokenAsync(client));
        const string csv = "Book Id,Title,Author,ISBN13\r\n990001,API Import Book,API Author,=\"9781234567890\"\r\n";

        var firstPreview = await PreviewAsync(client, csv);
        var firstRow = firstPreview.RootElement.GetProperty("rows")[0];
        Assert.True(firstRow.GetProperty("selected").GetBoolean());
        Assert.Equal(
            ["Audiobook", "Ebook"],
            firstRow.GetProperty("mediaFormats").EnumerateArray().Select(value => value.GetString()!).ToArray());
        Assert.Equal("new", firstRow.GetProperty("matchStatus").GetString());

        var firstCommit = await client.PostAsJsonAsync(
            $"/api/v1/catalog-imports/goodreads/{firstPreview.RootElement.GetProperty("batchId").GetString()}/commit",
            new
            {
                rows = new[]
                {
                    new
                    {
                        rowId = firstRow.GetProperty("rowId").GetString(),
                        selected = true,
                        mediaFormats = new[] { "Audiobook", "Ebook" },
                        resolvedBookId = (int?)null
                    }
                }
            });
        var firstCommitBody = await firstCommit.Content.ReadAsStringAsync();
        Assert.True(
            firstCommit.StatusCode == HttpStatusCode.OK,
            $"Goodreads commit failed: {(int)firstCommit.StatusCode} {firstCommitBody}");

        using (var firstSummary = JsonDocument.Parse(firstCommitBody))
        {
            // A plain import is additive: nothing is searched or grabbed until the user asks.
            Assert.Equal(0, firstSummary.RootElement.GetProperty("searchesScheduled").GetInt32());
        }

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BookmarkarrDbContext>();
            var imported = await db.Audiobooks.Include(book => book.Editions)
                .SingleAsync(book => book.GoodreadsId == "990001");
            var audiobookEdition = imported.Editions.Single(edition => edition.MediaType == EditionMediaType.Audiobook);
            var ebookEdition = imported.Editions.Single(edition => edition.MediaType == EditionMediaType.Ebook);
            Assert.True(audiobookEdition.Monitored);
            Assert.True(ebookEdition.Monitored);
            Assert.Equal(2, imported.Editions.Count);
            Assert.Equal(0, await db.Downloads.CountAsync(download => download.AudiobookId == imported.Id));
            Assert.Equal(
                Path.Join(audiobookEdition.RootPath!, "API Author", "API Import Book"),
                imported.BasePath);

            // An audiobook profile scores codecs and bitrates that mean nothing for an
            // ebook, so the ebook edition must never inherit the audiobook default.
            var audiobookDefault = await db.QualityProfiles
                .FirstOrDefaultAsync(profile => profile.IsDefault && profile.MediaType == EditionMediaType.Audiobook);
            if (audiobookDefault is not null)
            {
                Assert.NotEqual(audiobookDefault.Id, ebookEdition.QualityProfileId);
            }

            var ebookDefault = await db.QualityProfiles
                .FirstOrDefaultAsync(profile => profile.IsDefault && profile.MediaType == EditionMediaType.Ebook);
            Assert.Equal(ebookDefault?.Id, ebookEdition.QualityProfileId);
        }

        // Strict mock with no setup: any search attempt would have thrown.
        downloadServiceMock.Verify(service => service.SearchAndDownloadAsync(It.IsAny<int>()), Times.Never);

        var secondPreview = await PreviewAsync(client, csv);
        var secondRow = secondPreview.RootElement.GetProperty("rows")[0];
        Assert.Equal("matched", secondRow.GetProperty("matchStatus").GetString());
        var secondCommit = await client.PostAsJsonAsync(
            $"/api/v1/catalog-imports/goodreads/{secondPreview.RootElement.GetProperty("batchId").GetString()}/commit",
            new
            {
                rows = new[]
                {
                    new
                    {
                        rowId = secondRow.GetProperty("rowId").GetString(),
                        selected = true,
                        mediaFormats = new[] { "Audiobook", "Ebook" },
                        resolvedBookId = (int?)null
                    }
                }
            });
        Assert.Equal(HttpStatusCode.OK, secondCommit.StatusCode);
        using var secondSummary = JsonDocument.Parse(await secondCommit.Content.ReadAsStringAsync());
        Assert.Equal(0, secondSummary.RootElement.GetProperty("createdBooks").GetInt32());
        Assert.Equal(0, secondSummary.RootElement.GetProperty("createdEditions").GetInt32());

        var absentPreview = await PreviewAsync(client, "Book Id,Title,Author\n990002,Another API Book,Another Author\n");
        var absentRow = absentPreview.RootElement.GetProperty("rows")[0];
        var absentCommit = await client.PostAsJsonAsync(
            $"/api/v1/catalog-imports/goodreads/{absentPreview.RootElement.GetProperty("batchId").GetString()}/commit",
            new
            {
                rows = new[]
                {
                    new
                    {
                        rowId = absentRow.GetProperty("rowId").GetString(),
                        selected = true,
                        mediaFormats = new[] { "Ebook" },
                        resolvedBookId = (int?)null
                    }
                }
            });
        Assert.Equal(HttpStatusCode.OK, absentCommit.StatusCode);

        using var verificationScope = factory.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<BookmarkarrDbContext>();
        var original = await verificationDb.Audiobooks.Include(book => book.Editions)
            .SingleAsync(book => book.GoodreadsId == "990001");
        Assert.All(original.Editions, edition => Assert.True(edition.Monitored));
        downloadServiceMock.Verify(service => service.SearchAndDownloadAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Commit_WithSearchAfterImportOptIn_SchedulesSearchesForAudiobookEditionsOnly()
    {
        var searched = new System.Collections.Concurrent.ConcurrentBag<int>();
        var firstSearch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var downloadServiceMock = new Mock<IDownloadService>();
        downloadServiceMock
            .Setup(service => service.SearchAndDownloadAsync(It.IsAny<int>()))
            .Callback<int>(audiobookId =>
            {
                searched.Add(audiobookId);
                firstSearch.TrySetResult();
            })
            .ReturnsAsync(new SearchAndDownloadResult
            {
                Success = true,
                Message = "Queued",
                DownloadId = "gd-optin-1",
                IndexerUsed = "Search"
            });

        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDownloadService>();
                services.AddSingleton(downloadServiceMock.Object);
            }));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", await GetAntiforgeryTokenAsync(client));
        const string csv = "Book Id,Title,Author\r\n990101,Opt In Book,Opt In Author\r\n";

        var preview = await PreviewAsync(client, csv);
        var row = preview.RootElement.GetProperty("rows")[0];
        var commit = await client.PostAsJsonAsync(
            $"/api/v1/catalog-imports/goodreads/{preview.RootElement.GetProperty("batchId").GetString()}/commit",
            new
            {
                rows = new[]
                {
                    new
                    {
                        rowId = row.GetProperty("rowId").GetString(),
                        selected = true,
                        mediaFormats = new[] { "Audiobook", "Ebook" },
                        resolvedBookId = (int?)null
                    }
                },
                searchAfterImport = true
            });

        Assert.Equal(HttpStatusCode.OK, commit.StatusCode);
        using var summary = JsonDocument.Parse(await commit.Content.ReadAsStringAsync());

        // Only the audiobook edition is searchable, so a both-formats row schedules one book.
        Assert.Equal(1, summary.RootElement.GetProperty("searchesScheduled").GetInt32());

        // Scheduling is deliberately off the request thread, so wait for it to land.
        var completed = await Task.WhenAny(firstSearch.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(firstSearch.Task, completed);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BookmarkarrDbContext>();
        var imported = await db.Audiobooks.SingleAsync(book => book.GoodreadsId == "990101");
        Assert.Equal([imported.Id], searched.Distinct().ToArray());
    }

    [Fact]
    public async Task AmbiguousExactMatch_MustBeResolvedBeforeCommit()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", await GetAntiforgeryTokenAsync(client));
        int selectedBookId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BookmarkarrDbContext>();
            var first = new Audiobook { Title = "Ambiguous API Title", Authors = ["Shared API Author"], Editions = [] };
            var second = new Audiobook { Title = "Ambiguous API Title", Authors = ["Shared API Author"], Editions = [] };
            db.Audiobooks.AddRange(first, second);
            await db.SaveChangesAsync();
            selectedBookId = first.Id;
        }

        var preview = await PreviewAsync(client, "Book Id,Title,Author\n,Ambiguous API Title,Shared API Author\n");
        var row = preview.RootElement.GetProperty("rows")[0];
        Assert.Equal("ambiguous", row.GetProperty("matchStatus").GetString());
        Assert.Equal(2, row.GetProperty("matchCandidates").GetArrayLength());
        var batchId = preview.RootElement.GetProperty("batchId").GetString();
        var rowId = row.GetProperty("rowId").GetString();

        var unresolved = await client.PostAsJsonAsync($"/api/v1/catalog-imports/goodreads/{batchId}/commit", new
        {
            rows = new[] { new { rowId, selected = true, mediaFormats = new[] { "Ebook" }, resolvedBookId = (int?)null } }
        });
        Assert.Equal(HttpStatusCode.Conflict, unresolved.StatusCode);

        var resolved = await client.PostAsJsonAsync($"/api/v1/catalog-imports/goodreads/{batchId}/commit", new
        {
            rows = new[] { new { rowId, selected = true, mediaFormats = new[] { "Ebook" }, resolvedBookId = selectedBookId } }
        });
        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);

        using var verificationScope = _factory.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<BookmarkarrDbContext>();
        Assert.Single(await verificationDb.BookEditions.Where(edition =>
            edition.BookId == selectedBookId && edition.MediaType == EditionMediaType.Ebook).ToListAsync());
    }

    private static async Task<JsonDocument> PreviewAsync(HttpClient client, string csv)
    {
        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", "goodreads.csv");
        var response = await client.PostAsync("/api/v1/catalog-imports/goodreads/preview", form);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Preview failed: {(int)response.StatusCode} {body}");
        return JsonDocument.Parse(body);
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/antiforgery/token");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var token = json.RootElement.GetProperty("token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        return token!;
    }
}
