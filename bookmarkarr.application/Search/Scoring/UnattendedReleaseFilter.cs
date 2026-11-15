/*
 * Bookmarkarr - Audiobook Management System
 * Copyright (C) 2024-2026 Bookmarkarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */

using Bookmarkarr.Domain.Search;

namespace Bookmarkarr.Application.Search.Scoring
{
    /// <summary>A release an unattended search would not take, and why.</summary>
    public sealed record ReleaseRefusal(string Title, ReleaseRejection Rejection, string Reason);

    /// <summary>What is left of a result list once it has been held against the wanted book.</summary>
    public sealed record UnattendedReleaseSelection(List<SearchResult> Accepted, List<ReleaseRefusal> Refused);

    /// <summary>
    /// Holds search results against the wanted book before any of them is scored.
    /// </summary>
    /// <remarks>
    /// Scoring ranks by format, seeders and age, and never asked whether a release was the book.
    /// This runs first and only for searches nobody is watching: a person choosing from a list can
    /// take a pack or an odd edition on purpose, and is not second-guessed.
    /// </remarks>
    public static class UnattendedReleaseFilter
    {
        public static UnattendedReleaseSelection Apply(Audiobook book, bool ebook, QualityProfile profile, IEnumerable<SearchResult> releases)
        {
            var wanted = WantedBook.From(book);
            var accepted = new List<SearchResult>();
            var refused = new List<ReleaseRefusal>();
            foreach (var release in releases)
            {
                var verdict = ReleaseRelevanceEvaluator.Evaluate(wanted, release.Title, release.Artist);
                if (!verdict.IsRelevant)
                {
                    refused.Add(new ReleaseRefusal(release.Title, verdict.Rejection, verdict.Detail));
                    continue;
                }

                var size = ReleaseSizeGuard.Check(release.Size, ebook, profile.MaximumSize);
                if (size != null)
                {
                    refused.Add(new ReleaseRefusal(release.Title, ReleaseRejection.None, size));
                    continue;
                }

                accepted.Add(release);
            }

            return new UnattendedReleaseSelection(accepted, refused);
        }

        /// <summary>"2 a different volume, 1 several books in one" — the refusals, counted by kind.</summary>
        public static string Summarize(IReadOnlyCollection<ReleaseRefusal> refused) =>
            string.Join(", ", refused
                .GroupBy(refusal => Label(refusal.Rejection))
                .OrderByDescending(group => group.Count())
                .Select(group => $"{group.Count()} {group.Key}"));

        private static string Label(ReleaseRejection rejection) => rejection switch
        {
            ReleaseRejection.TitleNotFound => "a different title",
            ReleaseRejection.DifferentTitle => "a longer title containing this one",
            ReleaseRejection.DifferentVolume => "a different volume",
            ReleaseRejection.DifferentAuthor => "by another author",
            ReleaseRejection.AuthorUnconfirmed => "with no author to confirm it",
            ReleaseRejection.MultiBookPack => "several books in one",
            ReleaseRejection.PartialRelease => "only part of the book",
            _ => "the wrong size"
        };
    }
}
