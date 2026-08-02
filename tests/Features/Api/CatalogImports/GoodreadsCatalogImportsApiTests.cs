/* Bookmarkarr is licensed under the GNU AGPL v3 or later. */
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Bookmarkarr.Domain.Books;
using Bookmarkarr.Tests.Mocks;
using Microsoft.EntityFrameworkCore;

namespace Bookmarkarr.Tests.Features.Api.CatalogImports;

public sealed class GoodreadsCatalogImportsApiTests : IClassFixture<BookmarkarrWebApplicationFactory>
{
    private readonly BookmarkarrWebApplicationFactory _factory;

    public GoodreadsCatalogImportsApiTests(BookmarkarrWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task PreviewAndCommit_DefaultBothEditions_IsAdditiveIdempotentAndDoesNotSearch()
    {
        using var client = _factory.CreateClient();
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
        Assert.Equal(HttpStatusCode.OK, firstCommit.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BookmarkarrDbContext>();
            var imported = await db.Audiobooks.Include(book => book.Editions)
                .SingleAsync(book => book.GoodreadsId == "990001");
            Assert.True(imported.Editions.Single(edition => edition.MediaType == EditionMediaType.Audiobook).Monitored);
            Assert.True(imported.Editions.Single(edition => edition.MediaType == EditionMediaType.Ebook).Monitored);
            Assert.Equal(2, imported.Editions.Count);
            Assert.Equal(0, await db.Downloads.CountAsync(download => download.AudiobookId == imported.Id));
        }

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

        using var verificationScope = _factory.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<BookmarkarrDbContext>();
        var original = await verificationDb.Audiobooks.Include(book => book.Editions)
            .SingleAsync(book => book.GoodreadsId == "990001");
        Assert.All(original.Editions, edition => Assert.True(edition.Monitored));
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
