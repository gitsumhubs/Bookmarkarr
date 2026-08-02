/*
 * Bookmarkarr - Audiobook Management System
 * Copyright (C) 2024-2026 Bookmarkarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using System.Text.RegularExpressions;

namespace Bookmarkarr.Application.Search.Filters;

/// <summary>
/// Applies a positive allowlist to indexer results. Unknown and unrelated
/// releases are rejected instead of being treated as audiobooks by default.
/// </summary>
public static class SearchContentTypeClassifier
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    public static bool IsAllowed(IndexerSearchResult result, SearchContentType contentType)
    {
        ArgumentNullException.ThrowIfNull(result);

        var category = result.Category ?? string.Empty;
        var searchableText = string.Join(' ', new[]
        {
            result.Title,
            result.Format,
            result.Quality,
            result.Description
        }.Where(value => !string.IsNullOrWhiteSpace(value)));

        var categorySaysAudiobook = HasAudiobookCategory(category);
        var categorySaysEbook = HasEbookCategory(category);

        // A specific non-book category is authoritative. This prevents a movie
        // release containing terms such as AAC, MP3, or EPUB from leaking in.
        if (HasExplicitUnrelatedCategory(category, categorySaysAudiobook, categorySaysEbook))
        {
            return false;
        }

        var hasAudiobook = categorySaysAudiobook || HasAudiobookSignal(searchableText);
        var hasEbook = categorySaysEbook || HasEbookSignal(searchableText, category);

        return contentType switch
        {
            // Audiobook + ebook bundles belong in audiobook mode.
            SearchContentType.Audiobook => hasAudiobook,
            // Ebook mode is intentionally ebook-only and excludes bundles.
            SearchContentType.Ebook => hasEbook && !hasAudiobook,
            _ => false
        };
    }

    private static bool HasAudiobookCategory(string category)
    {
        return Regex.IsMatch(category, @"\baudio[\s_-]*books?\b", Options)
               || HasCategoryNumber(category, "3030");
    }

    private static bool HasEbookCategory(string category)
    {
        return Regex.IsMatch(category, @"\be[\s_-]*books?\b|\belectronic[\s_-]+books?\b", Options)
               || HasCategoryNumber(category, "7020");
    }

    private static bool HasCategoryNumber(string category, string number)
    {
        return Regex.IsMatch(category, $@"(?<!\d){Regex.Escape(number)}(?!\d)", Options);
    }

    private static bool HasExplicitUnrelatedCategory(
        string category,
        bool categorySaysAudiobook,
        bool categorySaysEbook)
    {
        if (string.IsNullOrWhiteSpace(category) || categorySaysAudiobook || categorySaysEbook)
        {
            return false;
        }

        if (Regex.IsMatch(
                category,
                @"\b(?:movies?|tv|music|games?|apps?|software|xxx|comics?|magazines?|newspapers?)\b",
                Options))
        {
            return true;
        }

        // Generic book categories can still be classified from an explicit file
        // format in the title. Everything else requires an audiobook/ebook category.
        if (Regex.IsMatch(category, @"\bbooks?\b|\b7000\b", Options))
        {
            return false;
        }

        return true;
    }

    private static bool HasAudiobookSignal(string text)
    {
        return Regex.IsMatch(
            text,
            @"\baudio[\s_-]*books?\b|\baudible\b|\bunabridged\b|\babridged\b|\bnarrated\b|\bfull[\s_-]*cast\b|\baudio[\s_-]*(?:cd|cassette)\b|(?<![a-z0-9])m4b(?![a-z0-9])",
            Options);
    }

    private static bool HasEbookSignal(string text, string category)
    {
        if (Regex.IsMatch(
                text,
                @"\be[\s_-]*books?\b|\bkindle\b|(?<![a-z0-9])(?:epub|mobi|azw3?|fb2)(?![a-z0-9])",
                Options))
        {
            return true;
        }

        // PDF is too broad to trust without a generic book category.
        return Regex.IsMatch(category, @"\bbooks?\b|\b7000\b", Options)
               && Regex.IsMatch(text, @"(?<![a-z0-9])pdf(?![a-z0-9])", Options);
    }
}
