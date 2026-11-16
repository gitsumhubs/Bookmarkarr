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
    /// Refuses, for an unattended grab, a release that says it is in another language.
    /// </summary>
    /// <remarks>
    /// Scoring only docks a mismatched language fifteen points, which a well-formatted German
    /// release of the right title easily outscores. A search nobody is watching should not take it.
    /// Only a stated language counts: the indexer's own field, or a tag in brackets or capitals.
    /// A release that says nothing is not judged, since most names never mention a language.
    /// </remarks>
    public static class ReleaseLanguageGuard
    {
        /// <summary>Why the release is refused, or null when its language is acceptable or unknown.</summary>
        /// <param name="releaseLanguage">The language the indexer reported, if any.</param>
        /// <param name="releaseTitle">The release name, read for a language tag when the indexer gave none.</param>
        /// <param name="bookLanguage">The wanted book's own language, which wins when set.</param>
        /// <param name="preferredLanguages">The quality profile's languages, used when the book has none.</param>
        public static string? Check(string? releaseLanguage, string? releaseTitle, string? bookLanguage, IEnumerable<string>? preferredLanguages)
        {
            var stated = ReleaseLanguageDetector.FromField(releaseLanguage) ?? ReleaseLanguageDetector.FromText(releaseTitle);
            if (stated == null)
            {
                return null;
            }

            var wanted = string.IsNullOrWhiteSpace(bookLanguage)
                ? (preferredLanguages ?? []).Where(language => !string.IsNullOrWhiteSpace(language)).ToList()
                : [bookLanguage];
            if (wanted.Count == 0 || wanted.Any(language => ReleaseLanguageDetector.AreSameLanguage(stated, language)))
            {
                return null;
            }

            return $"in {stated}, not {string.Join(" or ", wanted)}";
        }
    }
}
