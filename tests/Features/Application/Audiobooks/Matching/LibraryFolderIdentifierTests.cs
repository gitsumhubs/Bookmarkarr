/*
 * Bookmarkarr - Audiobook Management System
 * Copyright (C) 2024-2026 Bookmarkarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

namespace Bookmarkarr.Tests.Features.Application.Audiobooks.Matching
{
    public class LibraryFolderIdentifierTests
    {
        private const string Root = "/audiobooks";

        private static readonly Dictionary<string, AudiobookshelfItem> NoAudiobookshelf = new();

        [Fact]
        public void Identify_PrefersAudiobookshelfOverTheFolderName()
        {
            // The folder says "Bobiverse" is the author. It is the series — the real author appears
            // nowhere in the path, which is exactly why the folder name cannot be trusted.
            const string path = "/audiobooks/Bobiverse/We Are Legion (We Are Bob) Bobiverse, Book 1";
            var index = new Dictionary<string, AudiobookshelfItem>(StringComparer.OrdinalIgnoreCase)
            {
                [path] = new(path, "We Are Legion (We Are Bob)", ["Dennis E. Taylor"], "Bobiverse", "1", "B01LWAESYQ", "2016")
            };

            var identity = LibraryFolderIdentifier.Identify(path, Root, index);

            Assert.Equal("We Are Legion (We Are Bob)", identity.Title);
            Assert.Equal("Dennis E. Taylor", identity.Author);
            Assert.Equal("Bobiverse", identity.Series);
            Assert.Equal("B01LWAESYQ", identity.Asin);
            Assert.Equal(FolderIdentitySource.AudiobookshelfAsin, identity.Source);
            Assert.True(identity.IsConfident);
        }

        [Fact]
        public void Identify_MarksAnAudiobookshelfMatchWithoutAnAsinAsLessExact()
        {
            const string path = "/audiobooks/Some Author/Some Book";
            var index = new Dictionary<string, AudiobookshelfItem>(StringComparer.OrdinalIgnoreCase)
            {
                [path] = new(path, "Some Book", ["Some Author"], null, null, null, null)
            };

            var identity = LibraryFolderIdentifier.Identify(path, Root, index);

            Assert.Equal(FolderIdentitySource.Audiobookshelf, identity.Source);
            // Still trustworthy enough to adopt: the match itself came from a real metadata lookup.
            Assert.True(identity.IsConfident);
        }

        [Fact]
        public void Identify_FallsBackToTheFolderNameWhenAudiobookshelfHasNothing()
        {
            var identity = LibraryFolderIdentifier.Identify(
                "/audiobooks/Amor Towles/A Gentleman in Moscow", Root, NoAudiobookshelf);

            Assert.Equal("A Gentleman in Moscow", identity.Title);
            Assert.Equal("Amor Towles", identity.Author);
            Assert.Equal(FolderIdentitySource.FolderName, identity.Source);
            // Never adopted silently — a folder name is a guess, however plausible it looks.
            Assert.False(identity.IsConfident);
        }

        [Fact]
        public void FromFolderName_HandlesABareTitleWithNoAuthorFolder()
        {
            var identity = LibraryFolderIdentifier.Identify("/audiobooks/Atomic Habits", Root, NoAudiobookshelf);

            Assert.Equal("Atomic Habits", identity.Title);
            Assert.Null(identity.Author);
        }

        [Fact]
        public void FromFolderName_StripsDecorationsAndPullsOutTheSeriesNumber()
        {
            var identity = LibraryFolderIdentifier.Identify(
                "/audiobooks/Dennis E. Taylor/For We Are Many Book 2 (Unabridged) [2017]", Root, NoAudiobookshelf);

            Assert.Equal("For We Are Many", identity.Title);
            Assert.Equal("2", identity.SeriesNumber);
            Assert.Equal("2017", identity.Year);
        }

        [Fact]
        public void FromFolderName_ReadsTheParentAsAuthorEvenWhenItIsWrong()
        {
            // Documents the known limitation: this returns "DC Comics" as the author, which is a
            // publisher. It is why folder-name identities go to review instead of being adopted.
            var identity = LibraryFolderIdentifier.Identify(
                "/audiobooks/DC Comics/The Flash - Stop Motion", Root, NoAudiobookshelf);

            Assert.Equal("DC Comics", identity.Author);
            Assert.False(identity.IsConfident);
        }

        [Fact]
        public void Identify_MatchesRegardlessOfTrailingSeparatorOrCase()
        {
            const string reported = "/audiobooks/Author/Book";
            var index = new Dictionary<string, AudiobookshelfItem>(StringComparer.OrdinalIgnoreCase)
            {
                [reported] = new(reported, "Book", ["Author"], null, null, "ASIN1", null)
            };

            var identity = LibraryFolderIdentifier.Identify("/audiobooks/Author/Book/", Root, index);

            Assert.Equal("ASIN1", identity.Asin);
        }

        [Fact]
        public void Identify_ReturnsUnknownForAnEmptyPath()
        {
            var identity = LibraryFolderIdentifier.Identify(string.Empty, Root, NoAudiobookshelf);

            Assert.Equal(FolderIdentitySource.None, identity.Source);
            Assert.False(identity.IsConfident);
        }
        [Fact]
        public void Identify_UsesTheAudiobookshelfItemForADiscSubfolder()
        {
            // A book stored as Disc 1 / Disc 2 is discovered as two folders by a deepest-folder
            // walk. Audiobookshelf holds one item for the parent, and that boundary is the right
            // one — otherwise the book is adopted twice.
            const string item = "/audiobooks/Author/Long Book";
            var index = new Dictionary<string, AudiobookshelfItem>(StringComparer.OrdinalIgnoreCase)
            {
                [item] = new(item, "Long Book", ["Author"], null, null, "ASIN9", null)
            };

            var parent = LibraryFolderIdentifier.Identify(item, Root, index);
            var disc = LibraryFolderIdentifier.Identify($"{item}/Disc 1", Root, index);

            Assert.Equal("ASIN9", parent.Asin);
            // The subfolder itself is not an Audiobookshelf item, so identification alone cannot
            // resolve it — the workflow collapses it onto the parent before identifying.
            Assert.Equal(FolderIdentitySource.FolderName, disc.Source);
        }
    }
}
