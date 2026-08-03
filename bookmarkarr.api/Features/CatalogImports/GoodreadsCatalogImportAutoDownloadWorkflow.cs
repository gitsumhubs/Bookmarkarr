/* Bookmarkarr is licensed under the GNU AGPL v3 or later. */
namespace Bookmarkarr.Api.Features.CatalogImports;

/// <summary>
/// Runs the optional "search after import" pass for a committed Goodreads batch.
/// </summary>
/// <remarks>
/// This only runs when the user explicitly opts in on the commit request; a plain
/// Goodreads import stays additive and never grabs anything on its own.
///
/// The work is deliberately scheduled off the request thread. A committed batch can
/// contain hundreds of rows, and searching each one inline would hold the HTTP request
/// open long past any reasonable client timeout. Each book is processed in its own DI
/// scope so background work never shares a DbContext with the request that started it.
/// </remarks>
public sealed class GoodreadsCatalogImportAutoDownloadWorkflow(
    IServiceScopeFactory scopeFactory,
    ILogger<GoodreadsCatalogImportAutoDownloadWorkflow> logger)
{
    /// <summary>
    /// Schedules background searches for the supplied books and returns immediately.
    /// </summary>
    /// <returns>The number of books scheduled.</returns>
    public int Schedule(IEnumerable<int> audiobookIds)
    {
        var ids = audiobookIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return 0;
        }

        logger.LogInformation(
            "Scheduling Goodreads search-after-import for {BookCount} book(s)", ids.Count);

        _ = Task.Run(() => RunAsync(ids));
        return ids.Count;
    }

    private async Task RunAsync(IReadOnlyList<int> audiobookIds)
    {
        foreach (var audiobookId in audiobookIds)
        {
            // A fresh scope per book keeps each search on its own DbContext and stops one
            // failure from poisoning the rest of the batch.
            try
            {
                using var scope = scopeFactory.CreateScope();
                var downloadService = scope.ServiceProvider.GetRequiredService<IDownloadService>();

                var result = await downloadService.SearchAndDownloadAsync(audiobookId);
                if (!result.Success)
                {
                    logger.LogInformation(
                        "Goodreads search-after-import did not grab a release for book {AudiobookId}: {Message}",
                        audiobookId,
                        result.Message);
                }
            }
            catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException or StackOverflowException))
            {
                logger.LogError(ex, "Goodreads search-after-import failed for book {AudiobookId}", audiobookId);
            }
        }

        logger.LogInformation(
            "Completed Goodreads search-after-import for {BookCount} book(s)", audiobookIds.Count);
    }
}
