/* Bookmarkarr is licensed under the GNU AGPL v3 or later. */
using Bookmarkarr.Domain.Books;

namespace Bookmarkarr.Tests.Features.Domain.Books;

public sealed class BookEditionTests
{
    [Theory]
    [InlineData("chapter.m4b", true, false)]
    [InlineData("chapter.MP3", true, false)]
    [InlineData("novel.epub", false, true)]
    [InlineData("novel.AZW3", false, true)]
    [InlineData("comic.cbz", false, false)]
    [InlineData("movie.mkv", false, false)]
    public void File_registration_is_strictly_media_typed(string path, bool audio, bool ebook)
    {
        Assert.Equal(audio, EditionFileTypes.IsAudio(path));
        Assert.Equal(ebook, EditionFileTypes.IsEbook(path));
    }

    [Fact]
    public void Editions_have_independent_monitoring_quality_roots_and_wanted_state()
    {
        var audiobook = BookEdition.Create(12, EditionMediaType.Audiobook);
        var ebook = BookEdition.Create(12, EditionMediaType.Ebook);
        audiobook.QualityProfileId = 7;
        audiobook.RootPath = "/audiobooks";
        ebook.QualityProfileId = 9;
        ebook.RootPath = "/ebooks";
        ebook.Monitored = false;

        Assert.True(audiobook.IsWanted);
        Assert.False(ebook.IsWanted);
        Assert.NotEqual(audiobook.QualityProfileId, ebook.QualityProfileId);
        Assert.NotEqual(audiobook.RootPath, ebook.RootPath);
    }

    [Theory]
    [InlineData(EditionWantedStatus.Missing, true)]
    [InlineData(EditionWantedStatus.Queued, true)]
    [InlineData(EditionWantedStatus.Downloading, true)]
    [InlineData(EditionWantedStatus.UpgradeAvailable, true)]
    [InlineData(EditionWantedStatus.Imported, false)]
    [InlineData(EditionWantedStatus.Unmonitored, false)]
    public void Wanted_state_includes_active_download_statuses(EditionWantedStatus status, bool expected)
    {
        var edition = BookEdition.Create(42, EditionMediaType.Audiobook);
        edition.Status = status;

        Assert.Equal(expected, edition.IsWanted);
    }
}
