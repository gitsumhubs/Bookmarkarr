/*
 * Bookmarkarr - Audiobook Management System
 * Copyright (C) 2024-2026 Bookmarkarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Bookmarkarr.Domain.Common;

namespace Bookmarkarr.Api.Features.Library;

/// <summary>What adopting one folder would do.</summary>
public enum AdoptionAction
{
    /// <summary>Already tracked; nothing to do.</summary>
    AlreadyTracked,

    /// <summary>A book matching this identity exists but has no files registered here.</summary>
    LinkToExistingBook,

    /// <summary>Confidently identified and not tracked — a new book would be created.</summary>
    CreateBook,

    /// <summary>Identified only from folder naming; needs a human before anything is created.</summary>
    NeedsReview
}

/// <summary>One folder's adoption plan.</summary>
public sealed record AdoptionCandidate(
    string Path,
    string? Title,
    string? Author,
    string? Series,
    string? Asin,
    int FileCount,
    long Size,
    string IdentitySource,
    AdoptionAction Action,
    int? ExistingBookId);

/// <summary>Summary of an adoption pass.</summary>
public sealed record AdoptionPlan(
    bool DryRun,
    int FoldersScanned,
    int AlreadyTracked,
    int WouldCreate,
    int WouldLink,
    int NeedsReview,
    int Created,
    int Linked,
    bool AudiobookshelfUsed,
    int AudiobookshelfMatches,
    List<AdoptionCandidate> Candidates);

/// <summary>
/// Builds and optionally commits a plan for adopting library folders Bookmarkarr does not know
/// about.
///
/// Dry-run-first for the same reason status reconciliation is: this creates records from
/// heuristics over other people's folder naming, and a wrong guess is far cheaper to discard in a
/// preview than to unpick from the database afterwards.
/// </summary>
public sealed class LibraryAdoptionWorkflow(
    IAudiobookRepository audiobookRepository,
    IAudiobookshelfClient audiobookshelfClient,
    IFileSystem fileSystem,
    ILogger<LibraryAdoptionWorkflow> logger)
{
    /// <summary>Folders holding at least this many audio files are treated as a book.</summary>
    private const int MinimumAudioFiles = 1;

    public async Task<AdoptionPlan> PlanAsync(
        string rootPath,
        bool dryRun,
        int limit,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !fileSystem.DirectoryExists(rootPath))
        {
            throw new DirectoryNotFoundException($"Root folder not found: {rootPath}");
        }

        var audiobookshelf = await audiobookshelfClient.GetLibraryIndexAsync(ct);
        var books = await audiobookRepository.GetAllAsync();

        var trackedPaths = new HashSet<string>(
            books
                .Where(book => !string.IsNullOrWhiteSpace(book.BasePath))
                .Select(book => NormalizePath(book.BasePath!)),
            StringComparer.OrdinalIgnoreCase);

        var candidates = new List<AdoptionCandidate>();
        var matchedByAudiobookshelf = 0;

        foreach (var folder in FindBookFolders(rootPath, ct).Take(limit))
        {
            ct.ThrowIfCancellationRequested();

            var identity = LibraryFolderIdentifier.Identify(folder.Path, rootPath, audiobookshelf);
            if (identity.IsConfident)
            {
                matchedByAudiobookshelf++;
            }

            var existing = FindExistingBook(books, identity);
            var action = DetermineAction(trackedPaths, folder.Path, identity, existing);

            candidates.Add(new AdoptionCandidate(
                folder.Path,
                identity.Title,
                identity.Author,
                identity.Series,
                identity.Asin,
                folder.FileCount,
                folder.Size,
                identity.Source.ToString(),
                action,
                existing?.Id));
        }

        var plan = new AdoptionPlan(
            dryRun,
            candidates.Count,
            candidates.Count(candidate => candidate.Action == AdoptionAction.AlreadyTracked),
            candidates.Count(candidate => candidate.Action == AdoptionAction.CreateBook),
            candidates.Count(candidate => candidate.Action == AdoptionAction.LinkToExistingBook),
            candidates.Count(candidate => candidate.Action == AdoptionAction.NeedsReview),
            Created: 0,
            Linked: 0,
            audiobookshelf.Count > 0,
            matchedByAudiobookshelf,
            candidates);

        if (dryRun)
        {
            logger.LogInformation(
                "Adoption dry run over {Root}: {Scanned} folder(s), {Create} to create, {Link} to link, {Review} needing review",
                rootPath, plan.FoldersScanned, plan.WouldCreate, plan.WouldLink, plan.NeedsReview);
            return plan;
        }

        return await CommitAsync(plan, ct);
    }

    private async Task<AdoptionPlan> CommitAsync(AdoptionPlan plan, CancellationToken ct)
    {
        var created = 0;
        var linked = 0;

        foreach (var candidate in plan.Candidates)
        {
            ct.ThrowIfCancellationRequested();

            switch (candidate.Action)
            {
                case AdoptionAction.LinkToExistingBook when candidate.ExistingBookId is int bookId:
                    if (await LinkAsync(bookId, candidate))
                    {
                        linked++;
                    }
                    break;

                case AdoptionAction.CreateBook:
                    if (await CreateAsync(candidate))
                    {
                        created++;
                    }
                    break;

                // Review candidates are deliberately untouched: folder naming alone is not
                // evidence enough to create a book from.
                default:
                    break;
            }
        }

        logger.LogInformation("Adoption committed: {Created} created, {Linked} linked", created, linked);
        return plan with { Created = created, Linked = linked };
    }

    private async Task<bool> LinkAsync(int bookId, AdoptionCandidate candidate)
    {
        try
        {
            var book = await audiobookRepository.GetByIdAsync(bookId);
            if (book is null)
            {
                return false;
            }

            book.BasePath = candidate.Path;
            await audiobookRepository.UpdateAsync(book);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Could not link {Path} to book {BookId}", candidate.Path, bookId);
            return false;
        }
    }

    private async Task<bool> CreateAsync(AdoptionCandidate candidate)
    {
        try
        {
            var book = new Audiobook
            {
                Title = candidate.Title,
                Authors = string.IsNullOrWhiteSpace(candidate.Author) ? null : [candidate.Author],
                Series = candidate.Series,
                Asin = candidate.Asin,
                BasePath = candidate.Path,
                // Adopted books are not monitored on creation. They already exist on disk, and
                // switching monitoring on before an operator has reviewed them would put the whole
                // adopted library into the search rotation at once.
                Monitored = false
            };

            await audiobookRepository.AddAsync(book);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Could not create a book for {Path}", candidate.Path);
            return false;
        }
    }

    private static AdoptionAction DetermineAction(
        HashSet<string> trackedPaths,
        string folderPath,
        FolderIdentity identity,
        Audiobook? existing)
    {
        if (trackedPaths.Contains(NormalizePath(folderPath)))
        {
            return AdoptionAction.AlreadyTracked;
        }

        if (existing is not null)
        {
            return AdoptionAction.LinkToExistingBook;
        }

        return identity.IsConfident ? AdoptionAction.CreateBook : AdoptionAction.NeedsReview;
    }

    private static Audiobook? FindExistingBook(List<Audiobook> books, FolderIdentity identity)
    {
        // ASIN first: it is the only exact key. Title matching is a fallback and is deliberately
        // paired with the author, because series entries share titles across authors constantly.
        if (!string.IsNullOrWhiteSpace(identity.Asin))
        {
            var byAsin = books.FirstOrDefault(book =>
                string.Equals(book.Asin, identity.Asin, StringComparison.OrdinalIgnoreCase));
            if (byAsin is not null)
            {
                return byAsin;
            }
        }

        if (string.IsNullOrWhiteSpace(identity.Title))
        {
            return null;
        }

        var normalizedTitle = TitleUtils.NormalizeTitle(identity.Title);

        return books.FirstOrDefault(book =>
            !string.IsNullOrWhiteSpace(book.Title)
            && string.Equals(TitleUtils.NormalizeTitle(book.Title), normalizedTitle, StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(identity.Author)
                || book.Authors == null
                || book.Authors.Count == 0
                || book.Authors.Any(author => string.Equals(author, identity.Author, StringComparison.OrdinalIgnoreCase))));
    }

    /// <summary>
    /// Walks the root and yields the deepest folders that directly contain audio files.
    ///
    /// Deepest-first matters: a book split into <c>Disc 1</c>/<c>Disc 2</c> subfolders would
    /// otherwise be adopted as two books, and an author folder holding loose files would swallow
    /// everything beneath it.
    /// </summary>
    private IEnumerable<(string Path, int FileCount, long Size)> FindBookFolders(
        string rootPath,
        CancellationToken ct)
    {
        var pending = new Stack<string>();
        pending.Push(rootPath);

        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var current = pending.Pop();

            List<string> subdirectories;
            List<string> audioFiles;
            try
            {
                subdirectories = [.. fileSystem.EnumerateDirectories(current)];
                audioFiles = [.. fileSystem.EnumerateFiles(current).Where(FileUtils.IsAudioFile)];
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception, "Skipping unreadable folder {Folder}", current);
                continue;
            }

            foreach (var subdirectory in subdirectories)
            {
                pending.Push(subdirectory);
            }

            if (audioFiles.Count < MinimumAudioFiles || string.Equals(current, rootPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            long size = 0;
            foreach (var file in audioFiles)
            {
                try
                {
                    size += fileSystem.GetFileLength(file);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // A single unreadable file should not exclude the folder.
                }
            }

            yield return (current, audioFiles.Count, size);
        }
    }

    private static string NormalizePath(string path) =>
        FileUtils.NormalizeStoredPath(path ?? string.Empty)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
