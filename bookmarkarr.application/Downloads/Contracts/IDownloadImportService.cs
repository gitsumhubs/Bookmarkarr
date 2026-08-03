
namespace Bookmarkarr.Application.Downloads.Contracts
{
    /// <param name="ForceArchiveExtraction">Extract archives even when the setting is off.</param>
    /// <param name="ContentType">Audiobook or ebook; selects the import branch.</param>
    /// <param name="LibraryRoot">Destination library root; falls back to the configured one.</param>
    /// <param name="CompletedFileActionOverride">
    /// Overrides the configured completed-file action for this import only. Library
    /// import lets the user choose copy/move per batch; downloads leave this null and
    /// keep using the configured default.
    /// </param>
    public sealed record DownloadImportOptions(
        bool ForceArchiveExtraction = false,
        string ContentType = DownloadContentTypes.Audiobook,
        string? LibraryRoot = null,
        FileAction? CompletedFileActionOverride = null);

    /// <summary>
    /// Download import responsible for processing a given download importation
    /// </summary>
    public interface IDownloadImportService
    {
        /// <summary>
        /// Import files for a given audiobook
        /// - Handles archives
        /// - Handles quality profiles
        /// - Handles naming patterns
        /// </summary>
        /// <param name="audiobook">Audiobook for which we are importing files</param>
        /// <param name="files">List of files to import as reported by the IDownloadClientGateway</param>
        /// <param name="ct"></param>
        /// <param name="options">Per-download import requirements that supplement global settings.</param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException">If arguments are not consistent (missing audiobook base path for example)</exception>
        /// <exception cref="IOException">Thrown if we are unable to process one archive</exception>
        Task<List<ImportResult>> ImportDownloadFilesAsync(
            Audiobook audiobook,
            List<string> files,
            CancellationToken ct = default,
            DownloadImportOptions? options = null);
    }
}
