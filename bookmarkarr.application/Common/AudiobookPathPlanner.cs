/* Bookmarkarr is licensed under the GNU AGPL v3 or later. */
using System.Text.RegularExpressions;
using Bookmarkarr.Domain.Common;

namespace Bookmarkarr.Application.Common;

/// <summary>
/// Derives the per-book audiobook directory shared by creation, repair, and import paths.
/// </summary>
public static class AudiobookPathPlanner
{
    /// <summary>Resolves an audiobook's stored or derived per-book destination.</summary>
    /// <param name="audiobook">Book metadata and its audiobook edition.</param>
    /// <param name="settings">Configured output root and naming patterns.</param>
    /// <param name="fileNamingService">Naming-pattern renderer.</param>
    /// <param name="basePath">The resolved per-book destination when successful.</param>
    /// <returns>True when a destination could be resolved.</returns>
    public static bool TryResolveBasePath(
        Audiobook audiobook,
        ApplicationSettings settings,
        IFileNamingService fileNamingService,
        out string basePath)
    {
        if (!string.IsNullOrWhiteSpace(audiobook.BasePath))
        {
            basePath = FileUtils.NormalizeStoredPath(audiobook.BasePath);
            return true;
        }

        var edition = audiobook.Editions
            .FirstOrDefault(candidate => candidate.MediaType == EditionMediaType.Audiobook);
        if (edition is null)
        {
            basePath = string.Empty;
            return false;
        }

        var rootPath = edition.RootFolder?.Path;
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            rootPath = edition.RootPath;
        }

        if (string.IsNullOrWhiteSpace(rootPath))
        {
            rootPath = settings.OutputPath;
        }

        if (string.IsNullOrWhiteSpace(rootPath))
        {
            basePath = string.Empty;
            return false;
        }

        var pattern = !string.IsNullOrWhiteSpace(settings.FolderNamingPattern)
            ? settings.FolderNamingPattern
            : settings.FileNamingPattern;
        basePath = ComputeBasePath(audiobook, rootPath, pattern, fileNamingService);
        return !string.IsNullOrWhiteSpace(basePath);
    }

    /// <summary>Computes a per-book directory from a library root and naming pattern.</summary>
    /// <param name="audiobook">Book metadata used by naming tokens.</param>
    /// <param name="rootPath">Library root or an already-derived per-book path.</param>
    /// <param name="namingPattern">Configured folder naming pattern.</param>
    /// <param name="fileNamingService">Naming-pattern renderer.</param>
    /// <returns>The normalized per-book destination.</returns>
    public static string ComputeBasePath(
        Audiobook audiobook,
        string rootPath,
        string namingPattern,
        IFileNamingService fileNamingService)
    {
        var directoryPattern = BuildDirectoryPattern(audiobook, namingPattern);
        var variables = new Dictionary<string, object>
        {
            { "Author", SanitizeDirectoryName(audiobook.Authors?.FirstOrDefault() ?? "Unknown Author") },
            { "Series", SanitizeDirectoryName(audiobook.Series ?? string.Empty) },
            { "Title", SanitizeDirectoryName(audiobook.Title ?? "Unknown Title") },
            { "Subtitle", SanitizeDirectoryName(audiobook.Subtitle ?? string.Empty) },
            { "Edition", SanitizeDirectoryName(audiobook.Edition ?? string.Empty) },
            { "Narrator", SanitizeDirectoryName(audiobook.Narrators is { Count: > 0 }
                ? string.Join(", ", audiobook.Narrators.Where(value => !string.IsNullOrWhiteSpace(value)))
                : string.Empty) },
            { "Publisher", SanitizeDirectoryName(audiobook.Publisher ?? string.Empty) },
            { "Language", SanitizeDirectoryName(audiobook.Language ?? string.Empty) },
            { "Asin", SanitizeDirectoryName(audiobook.Asin ?? string.Empty) },
            { "SeriesNumber", audiobook.SeriesNumber ?? string.Empty },
            { "Year", audiobook.PublishYear ?? string.Empty },
            { "Quality", string.Empty },
            { "DiskNumber", string.Empty },
            { "ChapterNumber", string.Empty }
        };

        var relativePath = fileNamingService.ApplyNamingPattern(directoryPattern, variables, false);
        var normalizedRoot = FileUtils.NormalizeStoredPath(rootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedRelative = relativePath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar)
            .Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.IsNullOrWhiteSpace(normalizedRelative))
        {
            return normalizedRoot;
        }

        // Older callers stored either the library root or the already-derived book
        // directory in BookEditions.RootPath. Accept both representations without
        // appending Author/Series/Title twice.
        if (string.Equals(normalizedRoot, normalizedRelative, StringComparison.OrdinalIgnoreCase)
            || normalizedRoot.EndsWith(
                Path.DirectorySeparatorChar + normalizedRelative,
                StringComparison.OrdinalIgnoreCase))
        {
            return normalizedRoot;
        }

        return FileUtils.CombineWithOptionalBase(normalizedRoot, normalizedRelative);
    }

    private static string BuildDirectoryPattern(Audiobook audiobook, string namingPattern)
    {
        var directoryPattern = string.IsNullOrWhiteSpace(namingPattern)
            ? "{Author}/{Title}"
            : namingPattern;
        directoryPattern = Regex.Replace(directoryPattern, @"\{DiskNumber[^}]*\}", string.Empty, RegexOptions.IgnoreCase);
        directoryPattern = Regex.Replace(directoryPattern, @"\{ChapterNumber[^}]*\}", string.Empty, RegexOptions.IgnoreCase);
        directoryPattern = CleanDirectoryPattern(directoryPattern);

        if (string.IsNullOrWhiteSpace(directoryPattern) || !directoryPattern.Contains('/'))
        {
            directoryPattern = "{Author}/{Title}";
        }

        if (!string.IsNullOrWhiteSpace(audiobook.Series) && !directoryPattern.Contains("{Series}"))
        {
            if (directoryPattern.Contains("{Author}/{Title}"))
            {
                directoryPattern = directoryPattern.Replace("{Author}/{Title}", "{Author}/{Series}/{Title}");
            }
            else if (directoryPattern.Contains("{Author}/"))
            {
                directoryPattern = directoryPattern.Replace("{Author}/", "{Author}/{Series}/");
            }
        }

        if (string.IsNullOrWhiteSpace(audiobook.Series))
        {
            directoryPattern = Regex.Replace(directoryPattern, @"\{Series[^}]*\}", string.Empty, RegexOptions.IgnoreCase);
            directoryPattern = CleanDirectoryPattern(directoryPattern);
        }

        return directoryPattern;
    }

    private static string SanitizeDirectoryName(string name)
    {
        foreach (var character in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(character, '_');
        }

        return name.Replace(":", "_")
            .Replace("*", "_")
            .Replace("?", "_")
            .Replace("\"", "_")
            .Replace("<", "_")
            .Replace(">", "_")
            .Replace("|", "_")
            .Trim();
    }

    private static string CleanDirectoryPattern(string pattern)
    {
        pattern = Regex.Replace(pattern, @"[\\/]\s*[\\/]", "/");
        pattern = Regex.Replace(pattern, @"^\s*[\\/]", string.Empty);
        return Regex.Replace(pattern, @"[\\/]\s*$", string.Empty);
    }
}
