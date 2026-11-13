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
    /// <summary>
    /// Words that turn up in release names without saying anything about which book it is.
    /// </summary>
    internal static class ReleaseNameVocabulary
    {
        public static readonly HashSet<string> Stopwords = new(StringComparer.Ordinal)
        {
            "the", "a", "an", "of", "and", "in", "on", "to", "for", "with", "by", "at", "from", "is", "as", "or"
        };

        /// <summary>Generic words for a series: "The Lightbringer Saga", "Riyria Chronicles".</summary>
        public static readonly HashSet<string> SeriesNouns = new(StringComparer.Ordinal)
        {
            "saga", "series", "chronicle", "chronicles", "trilogy", "cycle", "sequence", "universe", "novels"
        };

        /// <summary>What follows a number that is a quantity rather than a volume.</summary>
        public static readonly HashSet<string> Units = new(StringComparer.Ordinal)
        {
            "kbps", "kbit", "kb", "k", "hrs", "hr", "hours", "h", "mins", "min", "cds", "cd", "discs", "mb", "gb", "khz", "ch", "of"
        };

        /// <summary>Format, edition, production and uploader words.</summary>
        public static readonly HashSet<string> Noise = new(StringComparer.Ordinal)
        {
            "audiobook", "audiobooks", "audio", "book", "books", "bk", "vol", "volume", "part", "parts", "season", "episode",
            "episodes", "unabridged", "abridged", "unabr", "mp3", "m4b", "m4a", "flac", "aac", "opus", "ogg", "kbps", "vbr",
            "cbr", "mixed", "variable", "graphic", "graphicaudio", "full", "cast", "edition", "editions", "version", "updated",
            "dramatized", "dramatised", "dramatization", "adaptation", "chapterised", "chapterized", "untouched", "retail",
            "web", "repack", "req", "request", "nmr", "hd", "novel", "stories", "read", "reads", "narrated", "narrator", "bbc",
            "radio", "penguin", "audible", "original", "originals", "uk", "us", "dolby", "atmos", "mix", "binaural", "surround",
            "immersion", "tunnel", "soundbooth", "theater", "theatre", "litrpg", "gamelit", "adventure", "fantasy",
            "progression", "aka", "complete", "new", "remastered", "anniversary", "various", "isbn", "asin"
        };
    }
}
