/* Bookmarkarr is licensed under the GNU AGPL v3 or later. */

namespace Bookmarkarr.Tests.Features.Application.Downloads;

public sealed class DownloadSearchQueryBuilderTests
{
    private static Audiobook Book(string title, params string[] authors) =>
        new() { Title = title, Authors = [.. authors] };

    [Fact]
    public void PlainTitle_ProducesTitleAndAuthorFirst()
    {
        var candidates = DownloadSearchQueryBuilder.BuildCandidates(Book("Dune", "Frank Herbert"));

        Assert.Equal("Dune Frank Herbert", candidates[0]);
        Assert.Contains("Dune", candidates);
    }

    [Fact]
    public void UndecoratedTitle_DoesNotProduceDuplicateRungs()
    {
        var candidates = DownloadSearchQueryBuilder.BuildCandidates(Book("Dune", "Frank Herbert"));

        // Nothing to strip, so the stripped rung collapses into the normalized one.
        Assert.Equal(candidates.Distinct(StringComparer.OrdinalIgnoreCase).Count(), candidates.Count);
        Assert.Equal(2, candidates.Count);
    }

    [Fact]
    public void SeriesParenthetical_IsStrippedInABroaderRung()
    {
        var candidates = DownloadSearchQueryBuilder.BuildCandidates(
            Book("The Eye of the World (The Wheel of Time #1)", "Robert Jordan"));

        // Most specific first, still carrying the series text.
        Assert.Contains("Wheel of Time", candidates[0]);

        // A later rung drops the series decoration entirely.
        Assert.Contains(
            candidates,
            candidate => candidate.Equals("The Eye of the World Robert Jordan", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BookNumbering_IsRemovedFromTheStrippedRung()
    {
        var candidates = DownloadSearchQueryBuilder.BuildCandidates(
            Book("Mistborn: The Final Empire (Mistborn, Book 1)", "Brandon Sanderson"));

        Assert.Contains(
            candidates,
            candidate => !candidate.Contains("Book 1", StringComparison.OrdinalIgnoreCase)
                && candidate.Contains("Mistborn", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SmartQuotesAndDashes_AreFoldedToAscii()
    {
        var candidates = DownloadSearchQueryBuilder.BuildCandidates(
            Book("The Handmaid’s Tale — Special Edition", "Margaret Atwood"));

        Assert.All(candidates, candidate =>
        {
            Assert.DoesNotContain('’', candidate);
            Assert.DoesNotContain('—', candidate);
        });
        Assert.Contains(candidates, candidate => candidate.Contains("Handmaid", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TitleOnlyRung_IsAlwaysAvailableAsTheBroadestAttempt()
    {
        var candidates = DownloadSearchQueryBuilder.BuildCandidates(
            Book("Neuromancer (Sprawl #1)", "William Gibson"));

        // The broadest rung drops the author so a mismatched author spelling cannot
        // suppress every result.
        Assert.Contains(
            candidates,
            candidate => !candidate.Contains("Gibson", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AuthorIsNeverQueriedAlone()
    {
        var candidates = DownloadSearchQueryBuilder.BuildCandidates(
            Book("Neuromancer (Sprawl #1)", "William Gibson"));

        // Querying by author alone would return unrelated books.
        Assert.DoesNotContain(
            candidates,
            candidate => candidate.Equals("William Gibson", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TitleThatIsEntirelyDecoration_StillProducesAUsableQuery()
    {
        var candidates = DownloadSearchQueryBuilder.BuildCandidates(Book("(Untitled Series #2)", "Some Author"));

        Assert.NotEmpty(candidates);
        Assert.All(candidates, candidate => Assert.False(string.IsNullOrWhiteSpace(candidate)));
    }

    [Fact]
    public void MissingAuthor_DoesNotProduceATrailingSpace()
    {
        var candidates = DownloadSearchQueryBuilder.BuildCandidates(Book("Solaris"));

        Assert.Equal("Solaris", Assert.Single(candidates));
    }

    [Fact]
    public void BuildStillReturnsTheMostSpecificQuery()
    {
        // Back-compat: existing callers of Build() keep the precise behaviour.
        Assert.Equal(
            "Dune Frank Herbert",
            DownloadSearchQueryBuilder.Build(Book("Dune", "Frank Herbert")));
    }

    [Fact]
    public void BuildCandidates_KeepsApostrophisedWordsIntact()
    {
        // Regression: "Carl's" became "Carl s", and AudioBookBay tokenizes on whitespace, so the
        // stray "Carl" + "s" never matched "Carls". Automatic search returned zero results for a
        // book the manual path — which strips rather than spaces — found immediately.
        var candidates = DownloadSearchQueryBuilder.BuildCandidates(
            Book("Carl's Doomsday Scenario", "Matt Dinniman"));

        Assert.Equal("Carls Doomsday Scenario Matt Dinniman", candidates[0]);
        Assert.DoesNotContain(candidates, candidate => candidate.Contains("Carl s", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildCandidates_TreatsSmartApostrophesTheSameWay()
    {
        var candidates = DownloadSearchQueryBuilder.BuildCandidates(Book("Carl\u2019s Doomsday Scenario"));

        Assert.Equal("Carls Doomsday Scenario", candidates[0]);
    }

    [Fact]
    public void BuildCandidates_StillSplitsOnGenuineSeparators()
    {
        // Colons and brackets divide words and must keep becoming spaces; only word-internal
        // marks are dropped.
        var candidates = DownloadSearchQueryBuilder.BuildCandidates(Book("World War Z: An Oral History"));

        Assert.Equal("World War Z An Oral History", candidates[0]);
    }
    [Fact]
    public void BuildCandidates_FallsBackToTheMainTitleWithoutItsSubtitle()
    {
        // Catalogues carry the full "Main Title: Long Subtitle"; indexers almost never do, so
        // without this rung a book with a subtitle returns nothing on every attempt.
        var candidates = DownloadSearchQueryBuilder.BuildCandidates(
            Book("World War Z: An Oral History of the Zombie War", "Max Brooks"));

        Assert.Contains("World War Z Max Brooks", candidates);
        Assert.Contains("World War Z", candidates);
        // The precise query still leads — a broader rung is only reached when it finds nothing.
        Assert.StartsWith("World War Z An Oral History", candidates[0]);
    }

    [Fact]
    public void BuildCandidates_WillNotShortenToATooGenericMainTitle()
    {
        // "It" matches everything and nothing, so the subtitle rung is skipped entirely.
        var candidates = DownloadSearchQueryBuilder.BuildCandidates(Book("It: A Novel", "Stephen King"));

        Assert.DoesNotContain("It", candidates);
        Assert.DoesNotContain("It Stephen King", candidates);
    }

    [Fact]
    public void BuildCandidates_OrdersRungsFromMostToLeastSpecific()
    {
        var candidates = DownloadSearchQueryBuilder.BuildCandidates(
            Book("Dune: Book One (Dune Chronicles, #1)", "Frank Herbert"));

        // Each rung drops something; the author survives longer than the decoration does.
        Assert.True(candidates.Count >= 3, $"expected a ladder, got {candidates.Count}");
        Assert.Contains("Frank Herbert", candidates[0]);
        Assert.DoesNotContain("Frank Herbert", candidates[^1]);
    }
    [Fact]
    public void BuildCandidates_EndsWithTheMostDistinctiveWordPlusAuthor()
    {
        // Some indexers return nothing for a full title they nonetheless hold, so the ladder ends
        // on one strong word plus the author rather than giving up.
        var candidates = DownloadSearchQueryBuilder.BuildCandidates(
            Book("The Butcher's Masquerade (Dungeon Crawler Carl, #5)", "Matt Dinniman"));

        Assert.Equal("Masquerade Matt Dinniman", candidates[^1]);
    }

    [Fact]
    public void BuildCandidates_DropsApostropheWordsInABroaderRung()
    {
        // Prowlarr's AudioBook Bay indexer searches with "&tt=1", matching whole words against a
        // title stored with a typographic apostrophe. Measured against the live indexer:
        // "The Butchers Masquerade Matt Dinniman" -> 0, "The Masquerade Matt Dinniman" -> 3, all
        // of them the wanted release. No spelling of the apostrophe word works, so it has to go.
        var candidates = DownloadSearchQueryBuilder.BuildCandidates(
            Book("The Butcher's Masquerade (Dungeon Crawler Carl, #5)", "Matt Dinniman"));

        Assert.Contains("The Masquerade Matt Dinniman", candidates);

        // And it stays a fallback: the precise title is still tried first.
        Assert.NotEqual("The Masquerade Matt Dinniman", candidates[0]);
    }

    [Fact]
    public void BuildCandidates_NeverPicksAnApostropheWordAsTheDistinctiveOne()
    {
        // "Blacksmiths" is the longest word, but it is exactly the form that cannot match.
        var candidates = DownloadSearchQueryBuilder.BuildCandidates(
            Book("The Blacksmith's Forge", "Ann Lee"));

        Assert.Equal("Forge Ann Lee", candidates[^1]);
        Assert.DoesNotContain(candidates, candidate => candidate.Contains("Blacksmiths Ann Lee", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildCandidates_NeverQueriesTheAuthorAlone()
    {
        // A title that is nothing but an apostrophe word strips to empty; the rung must drop
        // rather than let the author stand in as the whole query.
        var candidates = DownloadSearchQueryBuilder.BuildCandidates(Book("O'Brien", "Some Author"));

        Assert.DoesNotContain("Some Author", candidates);
        Assert.All(candidates, candidate => Assert.Contains("OBrien", candidate, StringComparison.Ordinal));
    }

    [Fact]
    public void BuildCandidates_SkipsTheDistinctiveWordRungWithoutAnAuthor()
    {
        // A bare word is far too broad to be worth querying on its own.
        var candidates = DownloadSearchQueryBuilder.BuildCandidates(Book("The Butchers Masquerade"));

        Assert.DoesNotContain("Masquerade", candidates);
    }

    [Fact]
    public void BuildCandidates_IgnoresFillerWhenPickingTheDistinctiveWord()
    {
        var candidates = DownloadSearchQueryBuilder.BuildCandidates(
            Book("Dune Chronicles Unabridged", "Frank Herbert"));

        // "Chronicles" and "Unabridged" are longer but carry no weight.
        Assert.DoesNotContain("Chronicles Frank Herbert", candidates);
        Assert.DoesNotContain("Unabridged Frank Herbert", candidates);
    }
}
