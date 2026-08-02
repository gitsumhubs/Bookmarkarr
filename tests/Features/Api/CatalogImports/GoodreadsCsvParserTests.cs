/* Bookmarkarr is licensed under the GNU AGPL v3 or later. */
using System.Text;
using Bookmarkarr.Api.Features.CatalogImports;

namespace Bookmarkarr.Tests.Features.Api.CatalogImports;

public sealed class GoodreadsCsvParserTests
{
    [Fact]
    public async Task Parses_bom_quoted_fields_newlines_and_goodreads_isbn_wrappers()
    {
        const string csv = "\uFEFFBook Id,Title,Author,ISBN13\r\n42,\"A Title, With Comma\",\"Doe, Jane\",\"=\"\"9781234567890\"\"\"\r\n43,\"A quoted \"\"title\"\"\",\"Jane\r\nDoe\",\"\"\r\n";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var rows = await GoodreadsCsvParser.ParseAsync(stream, CancellationToken.None);

        Assert.Equal(2, rows.Count);
        Assert.Equal("A Title, With Comma", rows[0]["title"]);
        Assert.Equal("Doe, Jane", rows[0]["author"]);
        Assert.Equal("9781234567890", GoodreadsCsvParser.NormalizeIsbn(rows[0]["isbn13"]));
        Assert.Equal("A quoted \"title\"", rows[1]["title"]);
        Assert.Equal("Jane\r\nDoe", rows[1]["author"]);
    }

    [Theory]
    [InlineData("=\"978-1-2345-6789-0\"", "9781234567890")]
    [InlineData(" 0-123456-47-X ", "012345647X")]
    public void Normalizes_goodreads_isbn_values(string raw, string expected)
    {
        Assert.Equal(expected, GoodreadsCsvParser.NormalizeIsbn(raw));
    }

    [Fact]
    public async Task Rejects_non_goodreads_headers()
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("name,value\nA,B\n"));

        var error = await Assert.ThrowsAsync<FormatException>(() =>
            GoodreadsCsvParser.ParseAsync(stream, CancellationToken.None));

        Assert.Contains("not a Goodreads export", error.Message);
    }

    [Fact]
    public async Task Rejects_unterminated_quoted_field()
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Book Id,Title,Author\n1,\"Broken,Jane\n"));

        await Assert.ThrowsAsync<FormatException>(() =>
            GoodreadsCsvParser.ParseAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task Enforces_row_limit()
    {
        var csv = new StringBuilder("Book Id,Title,Author\n");
        for (var i = 0; i <= GoodreadsCsvParser.MaximumRows; i++)
            csv.Append(i).Append(",Title,Author\n");
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv.ToString()));

        var error = await Assert.ThrowsAsync<FormatException>(() =>
            GoodreadsCsvParser.ParseAsync(stream, CancellationToken.None));

        Assert.Contains("25,000", error.Message);
    }
}
