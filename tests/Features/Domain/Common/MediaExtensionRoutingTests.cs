/* Bookmarkarr is licensed under the GNU AGPL v3 or later. */
using Bookmarkarr.Domain.Books;

namespace Bookmarkarr.Tests.Features.Domain.Common;

public sealed class MediaExtensionRoutingTests
{
    [Theory]
    [InlineData("book.epub")]
    [InlineData("book.azw3")]
    [InlineData("book.mobi")]
    [InlineData("book.pdf")]
    public void EbookFormats_AreImportableForEbookEditions(string path)
    {
        Assert.True(FileUtils.IsImportableFor(
            path, EditionMediaType.Ebook, [".epub", ".azw3", ".mobi", ".pdf"]));
    }

    [Theory]
    [InlineData("book.epub")]
    [InlineData("book.pdf")]
    public void EbookFormats_AreRejectedForAudiobookEditions(string path)
    {
        // The whole point of splitting the lists: an ebook must never satisfy an
        // audiobook import, even when both media types are configured on the install.
        Assert.False(FileUtils.IsImportableFor(
            path, EditionMediaType.Audiobook, [".mp3", ".flac", ".m4a", ".m4b", ".ogg"]));
    }

    [Theory]
    [InlineData("chapter.m4b")]
    [InlineData("chapter.mp3")]
    public void AudioFormats_AreRejectedForEbookEditions(string path)
    {
        Assert.False(FileUtils.IsImportableFor(
            path, EditionMediaType.Ebook, [".epub", ".azw3", ".mobi", ".pdf"]));
    }

    [Theory]
    [InlineData("BOOK.EPUB", EditionMediaType.Ebook)]
    [InlineData("Chapter.M4B", EditionMediaType.Audiobook)]
    [InlineData("book.EpUb", EditionMediaType.Ebook)]
    public void ExtensionMatching_IsCaseInsensitive(string path, EditionMediaType mediaType)
    {
        var configured = mediaType == EditionMediaType.Ebook
            ? new List<string> { ".epub" }
            : [".m4b"];

        Assert.True(FileUtils.IsImportableFor(path, mediaType, configured));
    }

    [Fact]
    public void EmptyConfiguredList_FallsBackToBuiltInSetForThatMediaType()
    {
        // A blank setting must not disable the filter, and must not borrow the other
        // media type's set.
        Assert.True(FileUtils.IsImportableFor("book.epub", EditionMediaType.Ebook, null));
        Assert.True(FileUtils.IsImportableFor("book.djvu", EditionMediaType.Ebook, []));
        Assert.False(FileUtils.IsImportableFor("book.epub", EditionMediaType.Audiobook, []));

        Assert.True(FileUtils.IsImportableFor("chapter.m4b", EditionMediaType.Audiobook, null));
        Assert.False(FileUtils.IsImportableFor("chapter.m4b", EditionMediaType.Ebook, []));
    }

    [Fact]
    public void UnrelatedContent_IsNeverImportable()
    {
        foreach (var mediaType in new[] { EditionMediaType.Audiobook, EditionMediaType.Ebook })
        {
            Assert.False(FileUtils.IsImportableFor("cover.jpg", mediaType, null));
            Assert.False(FileUtils.IsImportableFor("readme.nfo", mediaType, null));
            Assert.False(FileUtils.IsImportableFor("movie.mkv", mediaType, null));
            Assert.False(FileUtils.IsImportableFor("noextension", mediaType, null));
        }
    }

    [Fact]
    public void ConfiguredListWins_OverBuiltInSet()
    {
        // A user who narrows the list should see the narrowing respected.
        Assert.False(FileUtils.IsImportableFor("book.pdf", EditionMediaType.Ebook, [".epub"]));

        // And one who adds a format should see it accepted.
        Assert.True(FileUtils.IsImportableFor("book.cbz", EditionMediaType.Ebook, [".epub", ".cbz"]));
    }

    [Fact]
    public void NormalizationAcceptsExtensionsWithoutLeadingDot()
    {
        Assert.True(FileUtils.IsImportableFor("book.epub", EditionMediaType.Ebook, ["epub"]));
    }
}
