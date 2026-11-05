/* Bookmarkarr is licensed under the GNU AGPL v3 or later. */
using Bookmarkarr.Domain.Audiobooks;
namespace Bookmarkarr.Domain.Books;

public static class BookEditionSynchronization
{
    public static void SetMonitoring(BookEdition edition, bool monitored)
    {
        edition.Monitored = monitored;
        if (!monitored) edition.Status = EditionWantedStatus.Unmonitored;
        else if (edition.Status == EditionWantedStatus.Unmonitored)
            edition.Status = edition.Files.Count > 0 ? EditionWantedStatus.Imported : EditionWantedStatus.Missing;
        edition.UpdatedAt = DateTime.UtcNow;
    }

    public static void SynchronizeAudioParent(BookEdition edition)
    {
        if (edition.MediaType != EditionMediaType.Audiobook) return;
        edition.Book.Monitored = edition.Monitored;
        edition.Book.QualityProfileId = edition.QualityProfileId;
        // RootPath is an edition root; BasePath is a specific book destination. Editing a flag
        // or a profile must never replace a book's destination with the entire library root.
    }

    public static void ApplyLegacyAudioSettings(Audiobook book, bool? monitored, int? qualityProfileId)
    {
        var audio = book.Editions.FirstOrDefault(e => e.MediaType == EditionMediaType.Audiobook);
        if (monitored.HasValue)
        {
            book.Monitored = monitored.Value;
            if (audio is not null) SetMonitoring(audio, monitored.Value);
        }
        if (qualityProfileId.HasValue)
        {
            book.QualityProfileId = qualityProfileId;
            if (audio is not null) audio.QualityProfileId = qualityProfileId;
        }
    }
}
