/* Bookmarkarr is licensed under the GNU AGPL v3 or later. */

namespace Bookmarkarr.Application.Downloads.Import;

public partial class DownloadImportService
{
    private static string SanitizeEbookPathSegment(string value)
    {
        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) return string.Empty;
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':']).ToHashSet();
        var sanitized = new string(trimmed.Select(character => invalid.Contains(character) ? '_' : character).ToArray())
            .Trim(' ', '.');
        return string.IsNullOrWhiteSpace(sanitized) ? "Unknown" : sanitized;
    }

    private static AudioMetadata BuildNamingMetadata(Audiobook? audiobook, AudioMetadata? extractedMetadata, string fallbackTitle)
    {
        if (audiobook != null)
        {
            var author = audiobook.Authors != null && audiobook.Authors.Any()
                ? string.Join(", ", audiobook.Authors)
                : FirstNonEmpty(ChooseAuthorFromMetadata(extractedMetadata), "Unknown Author");
            return new AudioMetadata
            {
                Title = FirstNonEmpty(audiobook.Title, extractedMetadata?.Title, fallbackTitle, "Unknown Title"),
                Subtitle = FirstNonEmpty(audiobook.Subtitle, extractedMetadata?.Subtitle),
                Edition = FirstNonEmpty(audiobook.Edition, extractedMetadata?.Edition),
                Artist = author,
                AlbumArtist = author,
                Album = FirstNonEmpty(extractedMetadata?.Album, audiobook.Title, fallbackTitle),
                Narrator = audiobook.Narrators != null && audiobook.Narrators.Any()
                    ? string.Join(", ", audiobook.Narrators.Where(n => !string.IsNullOrWhiteSpace(n)))
                    : extractedMetadata?.Narrator,
                Publisher = FirstNonEmpty(audiobook.Publisher, extractedMetadata?.Publisher),
                Language = FirstNonEmpty(audiobook.Language, extractedMetadata?.Language),
                Asin = FirstNonEmpty(audiobook.Asin, extractedMetadata?.Asin),
                Series = FirstNonEmpty(audiobook.Series, extractedMetadata?.Series),
                SeriesPosition = !string.IsNullOrWhiteSpace(audiobook.SeriesNumber) && decimal.TryParse(audiobook.SeriesNumber, out var position)
                    ? position : extractedMetadata?.SeriesPosition,
                Year = !string.IsNullOrWhiteSpace(audiobook.PublishYear) && int.TryParse(audiobook.PublishYear, out var year)
                    ? year : extractedMetadata?.Year,
                TrackNumber = extractedMetadata?.TrackNumber,
                DiscNumber = extractedMetadata?.DiscNumber,
                BitRate = extractedMetadata?.BitRate,
                Format = extractedMetadata?.Format
            };
        }

        if (extractedMetadata != null)
        {
            if (string.IsNullOrWhiteSpace(extractedMetadata.Title)) extractedMetadata.Title = fallbackTitle;
            if (string.IsNullOrWhiteSpace(extractedMetadata.Artist))
                extractedMetadata.Artist = FirstNonEmpty(ChooseAuthorFromMetadata(extractedMetadata), "Unknown Author");
            if (string.IsNullOrWhiteSpace(extractedMetadata.AlbumArtist)) extractedMetadata.AlbumArtist = extractedMetadata.Artist;
            return extractedMetadata;
        }

        return new AudioMetadata { Title = fallbackTitle, Artist = "Unknown Author", AlbumArtist = "Unknown Author" };
    }

    private static string ChooseAuthorFromMetadata(AudioMetadata? metadata)
    {
        if (metadata == null) return string.Empty;
        var primary = NonNarratorAuthorCandidate(metadata.Artist, metadata.Narrator);
        var alternate = NonNarratorAuthorCandidate(metadata.AlbumArtist, metadata.Narrator);
        if (string.IsNullOrWhiteSpace(primary)) return alternate;
        if (!string.IsNullOrWhiteSpace(metadata.Title) &&
            (primary.Contains(metadata.Title, StringComparison.OrdinalIgnoreCase) ||
             (!string.IsNullOrWhiteSpace(metadata.Series) && string.Equals(primary, metadata.Series, StringComparison.OrdinalIgnoreCase)) ||
             string.Equals(primary, metadata.Title, StringComparison.OrdinalIgnoreCase)))
            return string.IsNullOrWhiteSpace(alternate) ? primary : alternate;
        return primary;
    }

    private static string NonNarratorAuthorCandidate(string? candidate, string? narrator)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return string.Empty;
        var trimmed = candidate.Trim();
        return !string.IsNullOrWhiteSpace(narrator) && string.Equals(trimmed, narrator.Trim(), StringComparison.OrdinalIgnoreCase)
            ? string.Empty : trimmed;
    }

    private static string FirstNonEmpty(params string?[] candidates) =>
        candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate)) ?? string.Empty;
}
