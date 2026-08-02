namespace Bookmarkarr.Domain.Downloads;

public static class DownloadContentTypes
{
    public const string Audiobook = "audiobook";
    public const string Ebook = "ebook";

    public static bool IsEbook(Download download) =>
        string.Equals(
            download.GetMetadataString("ContentType"),
            Ebook,
            StringComparison.OrdinalIgnoreCase);
}
