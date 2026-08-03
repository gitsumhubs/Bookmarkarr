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
}
