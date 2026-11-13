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

namespace Bookmarkarr.Domain.Search
{
    /// <summary>Why a release is not the wanted book.</summary>
    public enum ReleaseRejection
    {
        None,
        TitleNotFound,
        DifferentTitle,
        DifferentVolume,
        DifferentAuthor,
        AuthorUnconfirmed,
        MultiBookPack,
        PartialRelease
    }

    public sealed record ReleaseRelevanceVerdict(bool IsRelevant, ReleaseRejection Rejection, string Detail)
    {
        public static ReleaseRelevanceVerdict Accept(string detail) => new(true, ReleaseRejection.None, detail);
        public static ReleaseRelevanceVerdict Reject(ReleaseRejection rejection, string detail) => new(false, rejection, detail);
    }

    /// <summary>
    /// Decides whether a release name is the wanted book, before anything is downloaded unattended.
    /// </summary>
    /// <remarks>
    /// Quality scoring never asked this. It ranked whatever a search returned by format, seeders
    /// and age, so a broad query that matched an 846 GB "Entire Audiobooks Collection" grabbed it
    /// for a single book, and "Reborn as an Assassin's Apprentice" was taken for "Assassin's
    /// Apprentice".
    ///
    /// The questions are asked in order, and the first "no" ends it:
    /// is it a pack; does it contain the title; is it only part of the book; does it number a
    /// different volume; is the title really part of a longer one; is it by someone else.
    ///
    /// It errs towards refusing. A refused release is still there for a manual search; a wrong
    /// one accepted is a download nobody asked for.
    /// </remarks>
    public static class ReleaseRelevanceEvaluator
    {
        public static ReleaseRelevanceVerdict Evaluate(WantedBook wanted, string? releaseTitle, string? releaseAuthor = null)
        {
            ArgumentNullException.ThrowIfNull(wanted);
            var identity = WantedBookIdentity.From(wanted);
            if (identity.TitleSegments.Count == 0)
            {
                return ReleaseRelevanceVerdict.Reject(ReleaseRejection.TitleNotFound, "the wanted book has no title to compare");
            }

            var tokens = ReleaseNameTokenizer.Tokenize(releaseTitle);
            if (tokens.Count == 0)
            {
                return ReleaseRelevanceVerdict.Reject(ReleaseRejection.TitleNotFound, "the release has no name");
            }

            var folded = ReleaseNameTokenizer.Fold(releaseTitle);
            var pack = ReleasePackDetector.FindPackMarker(folded, identity.OwnWords);
            if (pack != null)
            {
                return ReleaseRelevanceVerdict.Reject(ReleaseRejection.MultiBookPack, $"names several books: \"{pack}\"");
            }

            var matches = FindTitle(identity, tokens, out var subtitleMissing);
            if (matches.Count == 0)
            {
                return ReleaseRelevanceVerdict.Reject(ReleaseRejection.TitleNotFound, "the title is not in the release name");
            }

            foreach (var match in matches)
            {
                var following = ReleasePackDetector.FindPackAfterTitle(tokens, match.End, identity.OwnWords);
                if (following != null)
                {
                    return ReleaseRelevanceVerdict.Reject(ReleaseRejection.MultiBookPack, $"the title is followed by \"{following}\"");
                }
            }

            var partial = ReleasePackDetector.FindPartialMarker(folded);
            if (partial != null)
            {
                return ReleaseRelevanceVerdict.Reject(ReleaseRejection.PartialRelease, $"only part of the book: \"{partial}\"");
            }

            var volume = FindVolumeConflict(identity, tokens, matches);
            if (volume != null)
            {
                return ReleaseRelevanceVerdict.Reject(ReleaseRejection.DifferentVolume, volume);
            }

            var standing = matches.Where(match => FindForeignPrefix(identity, tokens, match) == null).ToList();
            if (standing.Count == 0)
            {
                var prefix = FindForeignPrefix(identity, tokens, matches[0]);
                return ReleaseRelevanceVerdict.Reject(ReleaseRejection.DifferentTitle, $"the title is part of a longer one: \"{prefix} …\"");
            }

            return CheckAuthor(identity, tokens, folded, releaseAuthor, standing[0], subtitleMissing);
        }

        /// <param name="FirstEnd">Where the first part of the title ends; a volume number can sit between the parts.</param>
        private readonly record struct TitleMatch(int Start, int FirstEnd, int End);

        /// <summary>
        /// Every place the wanted title appears. All its parts in order is the normal case. When
        /// the first part is the series name the rest is enough ("The Final Empire" for "Mistborn:
        /// The Final Empire"). A release that drops the subtitle is accepted on the main title if
        /// that is at least two words, and is then held to a confirmed author.
        /// </summary>
        private static List<TitleMatch> FindTitle(WantedBookIdentity identity, List<NameToken> tokens, out bool subtitleMissing)
        {
            subtitleMissing = false;
            var matches = FindSegments(tokens, identity.TitleSegments);
            if (matches.Count == 0 && identity.FirstSegmentIsSeriesLabel)
            {
                matches = FindSegments(tokens, identity.TitleSegments.Skip(1).ToList());
            }

            if (matches.Count == 0)
            {
                matches = FindSeriesPrefixedTitle(identity, tokens);
            }

            if (matches.Count == 0 && identity.TitleSegments.Count > 1 && identity.TitleSegments[0].Length >= 2)
            {
                matches = FindSegments(tokens, [identity.TitleSegments[0]]);
                subtitleMissing = matches.Count > 0;
            }

            return matches;
        }

        /// <summary>
        /// "Harry Potter and the Philosopher's Stone" written as "Harry Potter, Bk 1 - The
        /// Philosophers Stone": the series name and the rest of the title, apart.
        /// </summary>
        private static List<TitleMatch> FindSeriesPrefixedTitle(WantedBookIdentity identity, List<NameToken> tokens)
        {
            var first = identity.TitleSegments[0];
            foreach (var series in identity.SeriesNames.Where(series => series.Length < first.Length && first.Take(series.Length).SequenceEqual(series)))
            {
                var rest = first.Skip(series.Length).SkipWhile(ReleaseNameVocabulary.Stopwords.Contains).ToArray();
                if (rest.Length >= 2 && FindPhrase(tokens, series, 0) >= 0)
                {
                    var matches = FindSegments(tokens, [rest, .. identity.TitleSegments.Skip(1)]);
                    if (matches.Count > 0)
                    {
                        return matches;
                    }
                }
            }

            return [];
        }

        private static List<TitleMatch> FindSegments(List<NameToken> tokens, List<string[]> segments)
        {
            var matches = new List<TitleMatch>();
            if (segments.Count == 0)
            {
                return matches;
            }

            for (var start = FindPhrase(tokens, segments[0], 0); start >= 0; start = FindPhrase(tokens, segments[0], start + 1))
            {
                var firstEnd = start + segments[0].Length - 1;
                var end = firstEnd;
                var complete = true;
                foreach (var segment in segments.Skip(1))
                {
                    var next = FindPhrase(tokens, segment, end + 1);
                    if (next < 0)
                    {
                        complete = false;
                        break;
                    }

                    end = next + segment.Length - 1;
                }

                if (complete)
                {
                    matches.Add(new TitleMatch(start, firstEnd, end));
                }
            }

            return matches;
        }

        internal static int FindPhrase(List<NameToken> tokens, string[] phrase, int from)
        {
            for (var start = Math.Max(0, from); start + phrase.Length <= tokens.Count; start++)
            {
                var all = true;
                for (var offset = 0; offset < phrase.Length && all; offset++)
                {
                    all = SameWord(tokens[start + offset].Text, phrase[offset]);
                }

                if (all)
                {
                    return start;
                }
            }

            return -1;
        }

        /// <summary>
        /// Equal, a plural apart ("Shade" and "Shades", "Chronicle" and "Chronicles"), or for
        /// words of seven letters or more one letter apart past the first ("Rutherfurd" and
        /// "Rutherford"). Shorter words do not bend: "Eragon" is not "Dragon". Numbers never do.
        /// </summary>
        internal static bool SameWord(string left, string right)
        {
            if (left == right)
            {
                return true;
            }

            if (char.IsAsciiDigit(left[0]) || char.IsAsciiDigit(right[0]) || Math.Abs(left.Length - right.Length) > 1)
            {
                return false;
            }

            var (shorter, longer) = left.Length <= right.Length ? (left, right) : (right, left);
            if (shorter.Length >= 4 && longer.Length == shorter.Length + 1 && longer[^1] == 's' && longer.StartsWith(shorter, StringComparison.Ordinal))
            {
                return true;
            }

            if (shorter.Length < 7 || shorter[0] != longer[0])
            {
                return false;
            }

            var i = 0;
            while (i < shorter.Length && shorter[i] == longer[i])
            {
                i++;
            }

            return shorter.Length == longer.Length
                ? shorter.AsSpan(i + 1).SequenceEqual(longer.AsSpan(i + 1))
                : shorter.AsSpan(i).SequenceEqual(longer.AsSpan(i + 1));
        }

        /// <summary>
        /// A number written straight after the title, or after the series name, says which volume
        /// the release is. "Path of the Berserker 4" is not "Path of the Berserker".
        /// </summary>
        private static string? FindVolumeConflict(WantedBookIdentity identity, List<NameToken> tokens, List<TitleMatch> matches)
        {
            var stated = new List<decimal>();
            foreach (var end in matches.SelectMany(match => new[] { match.FirstEnd, match.End }).Distinct())
            {
                var afterTitle = ReadVolume(tokens, end + 1, allowSeriesNoun: false);
                if (afterTitle == null)
                {
                    continue;
                }

                var (number, marked, bracketed) = afterTitle.Value;
                stated.Add(number);
                var conflict = identity.Numbers.Count > 0
                    ? !identity.Numbers.Contains(number)
                    // Nothing known about the wanted volume: a release that numbers the title is a
                    // series named after it, and the wanted book is taken to be its first.
                    : number >= 2 && (marked || !bracketed);
                if (conflict)
                {
                    return $"the title is followed by volume {number}";
                }
            }

            if (identity.Numbers.Count == 0)
            {
                return null;
            }

            var statedForSeries = new List<decimal>();
            foreach (var series in identity.SeriesNames)
            {
                for (var start = FindPhrase(tokens, series, 0); start >= 0; start = FindPhrase(tokens, series, start + 1))
                {
                    var afterSeries = ReadVolume(tokens, start + series.Length, allowSeriesNoun: true);
                    if (afterSeries != null)
                    {
                        statedForSeries.Add(afterSeries.Value.Number);
                    }
                }
            }

            if (statedForSeries.Count > 0 && !statedForSeries.Any(identity.Numbers.Contains))
            {
                return $"the series is numbered {string.Join(", ", statedForSeries.Distinct())}";
            }

            return identity.NeedsStatedVolume && stated.Count == 0 && statedForSeries.Count == 0
                ? $"the release does not say it is volume {string.Join(" / ", identity.Numbers)}"
                : null;
        }

        private static (decimal Number, bool Marked, bool Bracketed)? ReadVolume(List<NameToken> tokens, int index, bool allowSeriesNoun)
        {
            if (allowSeriesNoun && index < tokens.Count && ReleaseNameVocabulary.SeriesNouns.Contains(tokens[index].Text))
            {
                index++;
            }

            var marked = index < tokens.Count && ReleaseNameTokenizer.VolumeMarkers.Contains(tokens[index].Text);
            if (marked)
            {
                index++;
            }

            if (index >= tokens.Count || !tokens[index].IsNumber || tokens[index].IsYear)
            {
                return null;
            }

            // "64 kbps", "16 hrs", "1 of 2": a quantity, not a volume.
            if (index + 1 < tokens.Count && ReleaseNameVocabulary.Units.Contains(tokens[index + 1].Text))
            {
                return null;
            }

            return (tokens[index].Number!.Value, marked, tokens[index].InBracket);
        }

        /// <summary>
        /// Words written directly in front of the title, with no separator, that the catalogue
        /// does not know: "Reborn as an" before "Assassin's Apprentice", "The City of Akul" before
        /// "The Legend of William Oh". The release is a different, longer title.
        /// </summary>
        private static string? FindForeignPrefix(WantedBookIdentity identity, List<NameToken> tokens, TitleMatch match)
        {
            var segment = tokens[match.Start].Segment;
            var foreign = new List<string>();
            for (var i = match.Start - 1; i >= 0 && tokens[i].Segment == segment; i--)
            {
                var word = tokens[i].Text;
                if (!tokens[i].IsNumber && !ReleaseNameVocabulary.Stopwords.Contains(word) && !ReleaseNameVocabulary.Noise.Contains(word)
                    && !identity.OwnWords.Any(own => SameWord(own, word)))
                {
                    foreign.Insert(0, word);
                }
            }

            return foreign.Count > 0 ? string.Join(" ", foreign) : null;
        }

        private static ReleaseRelevanceVerdict CheckAuthor(
            WantedBookIdentity identity, List<NameToken> tokens, string folded, string? releaseAuthor, TitleMatch match, bool subtitleMissing)
        {
            if (!identity.HasAuthors)
            {
                return ReleaseRelevanceVerdict.Accept("title matches; the wanted book has no author to compare");
            }

            var outsideTitle = tokens.Where((_, index) => index < match.Start || index > match.End).Select(token => token.Text)
                .Concat(ReleaseNameTokenizer.Tokenize(releaseAuthor).Select(token => token.Text)).ToList();
            if (identity.AuthorSurnames.Any(surname => outsideTitle.Any(word => IsSurname(word, surname))))
            {
                return ReleaseRelevanceVerdict.Accept(subtitleMissing ? "main title and author match" : "title and author match");
            }

            var other = ReleaseAuthorReader.FindNamedAuthor(folded, identity.OwnWords);
            if (other != null)
            {
                return ReleaseRelevanceVerdict.Reject(ReleaseRejection.DifferentAuthor, $"credited to \"{other}\"");
            }

            // No author anywhere in the name. A distinctive title can stand alone, and so can a
            // short one beside its series; one word by itself cannot.
            var distinctive = identity.TitleSegments.SelectMany(words => words).Count(word => !ReleaseNameVocabulary.Stopwords.Contains(word));
            var seriesNamed = identity.SeriesNames.Any(series => !identity.TitleIsSeriesLabel && FindPhrase(tokens, series, 0) >= 0);
            return !subtitleMissing && (distinctive >= 2 || seriesNamed)
                ? ReleaseRelevanceVerdict.Accept(seriesNamed ? "title and series match; the release names no author" : "title matches; the release names no author")
                : ReleaseRelevanceVerdict.Reject(ReleaseRejection.AuthorUnconfirmed, "the release names no author and the title alone is too short to be sure");
        }

        private static bool IsSurname(string word, string surname) =>
            SameWord(word, surname) || (surname.Length >= 6 && word.StartsWith(surname, StringComparison.Ordinal));
    }
}
