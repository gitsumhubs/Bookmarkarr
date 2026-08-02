/* Bookmarkarr is licensed under the GNU AGPL v3 or later. */
using System.ComponentModel.DataAnnotations;

namespace Bookmarkarr.Domain.Books;

public enum CatalogImportBatchStatus
{
    Preview = 0,
    Committed = 1,
    Expired = 2
}

/// <summary>Normalized, short-lived import state. Raw uploaded CSV bytes are never persisted.</summary>
public sealed class CatalogImportBatch
{
    [MaxLength(64)] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [MaxLength(32)] public string Source { get; set; } = "Goodreads";
    public CatalogImportBatchStatus Status { get; set; } = CatalogImportBatchStatus.Preview;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddHours(24);
    public DateTime? CommittedAt { get; set; }
    public string NormalizedRowsJson { get; set; } = "[]";
    public string? CommitSummaryJson { get; set; }
}
