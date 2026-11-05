/* Bookmarkarr is licensed under the GNU AGPL v3 or later. */
namespace Bookmarkarr.Api.Features.Library;

/// <summary>Omitted fields leave the stored value unchanged.</summary>
public sealed class LibraryUpdateRequest
{
    public int Id { get; set; }
    public string? Title { get; set; }
    public string? Subtitle { get; set; }
    public string? ImageUrl { get; set; }
    public string? PublishYear { get; set; }
    public string? PublishedDate { get; set; }
    public string? Series { get; set; }
    public string? SeriesNumber { get; set; }
    public string? Description { get; set; }
    public string? Asin { get; set; }
    public string? OpenLibraryId { get; set; }
    public string? Publisher { get; set; }
    public string? Language { get; set; }
    public string? Edition { get; set; }
    public string? Version { get; set; }
    public string? FilePath { get; set; }
    public string? BasePath { get; set; }
    public string? Quality { get; set; }
    public List<string>? Authors { get; set; }
    public List<string>? Genres { get; set; }
    public List<string>? Tags { get; set; }
    public List<string>? Narrators { get; set; }
    public List<string>? Isbn { get; set; }
    public List<AudiobookSeriesMembership>? SeriesMemberships { get; set; }
    public int? Runtime { get; set; }
    public long? FileSize { get; set; }
    public int? QualityProfileId { get; set; }
    public bool? Explicit { get; set; }
    public bool? Abridged { get; set; }
    public bool? Monitored { get; set; }
}
