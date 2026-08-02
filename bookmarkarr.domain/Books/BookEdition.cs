/*
 * Bookmarkarr - unified audiobook and ebook management
 * Copyright (C) 2026 Bookmarkarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bookmarkarr.Domain.Books;

public enum EditionMediaType
{
    Audiobook = 0,
    Ebook = 1
}

public enum EditionWantedStatus
{
    Unmonitored = 0,
    Missing = 1,
    Queued = 2,
    Downloading = 3,
    Imported = 4,
    UpgradeAvailable = 5
}

/// <summary>
/// Independently managed physical edition of a unified bibliographic book.
/// The parent remains the upstream-compatible Audiobook aggregate internally,
/// but is persisted and exposed as a Book by Bookmarkarr.
/// </summary>
public sealed class BookEdition
{
    public int Id { get; set; }
    public int BookId { get; set; }
    public Audiobook Book { get; set; } = null!;
    public EditionMediaType MediaType { get; set; }
    public bool Monitored { get; set; } = true;
    public bool UpgradeAllowed { get; set; } = true;
    public EditionWantedStatus Status { get; set; } = EditionWantedStatus.Missing;
    public int? QualityProfileId { get; set; }
    public QualityProfile? QualityProfile { get; set; }
    public int? RootFolderId { get; set; }
    public RootFolder? RootFolder { get; set; }
    public string? RootPath { get; set; }
    [MaxLength(100)] public string DownloadCategory { get; set; } = string.Empty;
    public DateTime? LastSearchTime { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<EditionFile> Files { get; set; } = [];

    [NotMapped]
    public bool IsWanted => Monitored && Status is EditionWantedStatus.Missing or EditionWantedStatus.UpgradeAvailable;

    public static BookEdition Create(int bookId, EditionMediaType mediaType, bool monitored = true) => new()
    {
        BookId = bookId,
        MediaType = mediaType,
        Monitored = monitored,
        Status = monitored ? EditionWantedStatus.Missing : EditionWantedStatus.Unmonitored,
        DownloadCategory = mediaType == EditionMediaType.Audiobook ? "audiobooks" : "ebooks"
    };
}

public sealed class EditionFile
{
    public int Id { get; set; }
    public int EditionId { get; set; }
    public BookEdition Edition { get; set; } = null!;
    [MaxLength(2048)] public string Path { get; set; } = string.Empty;
    [MaxLength(32)] public string Extension { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
}

public static class EditionFileTypes
{
    public static readonly HashSet<string> Audio = new(StringComparer.OrdinalIgnoreCase)
    {
        ".m4b", ".m4a", ".mp3", ".flac", ".ogg", ".opus", ".aac", ".wav", ".wma"
    };

    public static readonly HashSet<string> Ebook = new(StringComparer.OrdinalIgnoreCase)
    {
        ".epub", ".mobi", ".azw", ".azw3", ".pdf", ".djvu", ".fb2"
    };

    public static bool IsAllowed(EditionMediaType mediaType, string path) =>
        (mediaType == EditionMediaType.Audiobook ? Audio : Ebook).Contains(Path.GetExtension(path));

    public static bool IsAudio(string path) => Audio.Contains(Path.GetExtension(path));
    public static bool IsEbook(string path) => Ebook.Contains(Path.GetExtension(path));
}
