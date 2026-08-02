/*
 * Bookmarkarr - Audiobook Management System
 * Copyright (C) 2024-2026 Bookmarkarr Contributors
 */

using Bookmarkarr.Application.Search.Filters;

namespace Bookmarkarr.Tests.Features.Application.Search.Filters;

public class SearchContentTypeClassifierTests
{
    [Theory]
    [InlineData("Audio > Audiobook")]
    [InlineData("3030")]
    [InlineData("Audio Books")]
    public void Audiobook_category_is_only_allowed_in_audiobook_mode(string category)
    {
        var result = Result("A Novel MP3", category);

        Assert.True(SearchContentTypeClassifier.IsAllowed(result, SearchContentType.Audiobook));
        Assert.False(SearchContentTypeClassifier.IsAllowed(result, SearchContentType.Ebook));
    }

    [Theory]
    [InlineData("Books > Ebook")]
    [InlineData("7020")]
    [InlineData("Electronic Books")]
    public void Ebook_category_is_only_allowed_in_ebook_mode(string category)
    {
        var result = Result("A Novel EPUB", category);

        Assert.False(SearchContentTypeClassifier.IsAllowed(result, SearchContentType.Audiobook));
        Assert.True(SearchContentTypeClassifier.IsAllowed(result, SearchContentType.Ebook));
    }

    [Fact]
    public void Audiobook_ebook_bundle_is_allowed_only_in_audiobook_mode()
    {
        var result = Result("A Novel Audiobook MP3 + EPUB", "Books > Ebook");

        Assert.True(SearchContentTypeClassifier.IsAllowed(result, SearchContentType.Audiobook));
        Assert.False(SearchContentTypeClassifier.IsAllowed(result, SearchContentType.Ebook));
    }

    [Theory]
    [InlineData("Movies > HD", "A Novel MP3 64kbps")]
    [InlineData("TV > UHD", "A Novel Audiobook M4B")]
    [InlineData("Music > FLAC", "A Novel EPUB")]
    [InlineData("Books > Comics", "A Novel EPUB")]
    public void Explicit_unrelated_categories_are_never_allowed(string category, string title)
    {
        var result = Result(title, category);

        Assert.False(SearchContentTypeClassifier.IsAllowed(result, SearchContentType.Audiobook));
        Assert.False(SearchContentTypeClassifier.IsAllowed(result, SearchContentType.Ebook));
    }

    [Fact]
    public void Unknown_unclassified_result_is_never_allowed()
    {
        var result = Result("A Novel 2026", string.Empty);

        Assert.False(SearchContentTypeClassifier.IsAllowed(result, SearchContentType.Audiobook));
        Assert.False(SearchContentTypeClassifier.IsAllowed(result, SearchContentType.Ebook));
    }

    [Fact]
    public void Missing_category_can_use_audiobook_specific_format()
    {
        var result = Result("A Novel.m4b", string.Empty);

        Assert.True(SearchContentTypeClassifier.IsAllowed(result, SearchContentType.Audiobook));
        Assert.False(SearchContentTypeClassifier.IsAllowed(result, SearchContentType.Ebook));
    }

    [Fact]
    public void Missing_category_can_use_ebook_specific_format()
    {
        var result = Result("A Novel.epub", string.Empty);

        Assert.False(SearchContentTypeClassifier.IsAllowed(result, SearchContentType.Audiobook));
        Assert.True(SearchContentTypeClassifier.IsAllowed(result, SearchContentType.Ebook));
    }

    [Fact]
    public void Pdf_requires_a_generic_book_category()
    {
        Assert.True(SearchContentTypeClassifier.IsAllowed(
            Result("A Novel PDF", "Books"),
            SearchContentType.Ebook));
        Assert.False(SearchContentTypeClassifier.IsAllowed(
            Result("A Document PDF", string.Empty),
            SearchContentType.Ebook));
    }

    private static IndexerSearchResult Result(string title, string category)
    {
        return new IndexerSearchResult
        {
            Title = title,
            Category = category
        };
    }
}
