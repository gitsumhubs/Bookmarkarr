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

using System.Globalization;
using System.Text;

namespace Bookmarkarr.Api.Features.Prowlarr
{
    /// <summary>
    /// Builds the Cardigann indexer definition that replaces Prowlarr's compiled AudioBook Bay
    /// indexer.
    /// </summary>
    /// <remarks>
    /// The compiled indexer requests a single search URL and never walks the site's pagination,
    /// so every query is capped at one page — nine results — regardless of how many the site
    /// actually holds. This definition reads several pages instead.
    ///
    /// It is modelled on Prowlarr's own <c>ebookbay.yml</c>, which targets AudioBook Bay's sister
    /// site and shares its WordPress theme; the info-hash selector in particular is identical.
    ///
    /// Deliberately absent: any <c>User-Agent</c> header override. AudioBook Bay serves a 404 to
    /// clients identifying as Prowlarr, and forks such as prowlarr-abb already substitute a
    /// different agent for the whole process, which this definition inherits. Bookmarkarr does not
    /// add an override of its own.
    /// </remarks>
    internal static class AudiobookBayDefinition
    {
        /// <summary>Definition id, also the indexer name suffix shown in Prowlarr.</summary>
        public const string DefinitionId = "audiobookbay-custom";

        /// <summary>Indexer name Prowlarr will list once the definition loads.</summary>
        public const string IndexerName = "AudioBook Bay (Custom)";

        /// <summary>File name written under the Prowlarr definitions directory.</summary>
        public const string FileName = "audiobookbay.yml";

        /// <summary>
        /// Prowlarr reports a Cardigann indexer's origin as the definition file's name without its
        /// extension. That is how the patched indexer is recognised later: the display name is the
        /// operator's to edit, but this follows the file Bookmarkarr wrote.
        /// </summary>
        public const string DefinitionFileStem = "audiobookbay";

        /// <summary>Implementation name Prowlarr reports for indexers loaded from a definition file.</summary>
        public const string CardigannImplementation = "Cardigann";

        /// <summary>Prowlarr only preserves user definitions inside this subdirectory.</summary>
        public const string CustomSubdirectory = "Custom";

        /// <summary>Implementation name Prowlarr reports for the compiled, single-page indexer.</summary>
        public const string CompiledImplementation = "AudioBookBay";

        /// <summary>Results the site returns per page, used to project the post-patch result count.</summary>
        public const int ResultsPerPage = 9;

        public const int MinimumPages = 1;
        public const int MaximumPages = 10;
        public const int DefaultPages = 3;

        /// <summary>
        /// Renders the definition for <paramref name="pages"/> pages of results.
        /// </summary>
        public static string Build(int pages)
        {
            var pageCount = Math.Clamp(pages, MinimumPages, MaximumPages);

            var paths = new StringBuilder();
            paths.Append("    - path: /");
            for (var page = 2; page <= pageCount; page++)
            {
                paths.AppendLine();
                paths.Append(CultureInfo.InvariantCulture, $"    - path: \"page/{page}/\"");
            }

            // Three '$' so interpolation needs {{{ }}}: the definition is dense with Cardigann's
            // own {{ }} templates and regex braces, which must survive verbatim.
            return $$$"""
                ---
                # Managed by Bookmarkarr. Regenerated whenever the patch is re-applied.
                #
                # Prowlarr's compiled AudioBook Bay indexer reads only the first page of search
                # results, capping every query at {{{ResultsPerPage}}}. This definition reads {{{pageCount}}} page(s).
                id: {{{DefinitionId}}}
                name: {{{IndexerName}}}
                description: "AudioBook Bay (ABB) is a Public Torrent Tracker for AUDIOBOOKS"
                language: en-US
                type: public
                encoding: UTF-8
                requestDelay: 2
                links:
                  - https://audiobookbay.lu/
                legacylinks:
                  - http://audiobookbay.fi/

                caps:
                  categorymappings:
                    - {id: "Audiobook", cat: Audio/Audiobook, desc: "Audiobook"}

                  modes:
                    search: [q]
                    book-search: [q]

                settings: []

                download:
                  infohash:
                    hash:
                      selector: td:contains("Info Hash:") ~ td
                      filters:
                        - name: regexp
                          args: ([A-Fa-f0-9]{40})
                    title:
                      selector: div.postTitle h1
                      filters:
                        - name: trim
                        - name: validfilename

                search:
                  paths:
                {{{paths}}}
                  keywordsfilters:
                    - name: tolower
                  inputs:
                    s: "{{ .Keywords }}"

                  rows:
                    selector: div.post:has(div.postTitle a)

                  fields:
                    # Parsed before the title so the title can carry the format, matching how the
                    # compiled indexer presents results and keeping quality scoring working.
                    fileformat:
                      selector: div.postContent
                      optional: true
                      default: ""
                      filters:
                        - name: regexp
                          args: "Format:\\s*([A-Za-z0-9]+)"
                    booktitle:
                      selector: div.postTitle a
                      filters:
                        - name: trim
                    title:
                      text: "{{ .Result.booktitle }} [{{ .Result.fileformat }}]"
                      filters:
                        - name: re_replace
                          args: ["\\s*\\[\\]$", ""]
                    details:
                      selector: div.postTitle a
                      attribute: href
                    download:
                      selector: div.postTitle a
                      attribute: href
                    # Every AudioBook Bay post is an audiobook, but the site's own category field
                    # holds genre tags such as "LitRPG". Left unmapped, Bookmarkarr's content-type
                    # filter recognises neither an audiobook nor an ebook category and discards
                    # every result, so the type is stated explicitly here.
                    category:
                      text: "Audiobook"
                    size:
                      selector: div.postContent
                      optional: true
                      default: 0
                      filters:
                        - name: regexp
                          args: "File Size:\\s*([\\d.]+\\s*[KMGT]B)"
                    date:
                      selector: div.postContent
                      optional: true
                      default: now
                      filters:
                        - name: regexp
                          args: "Posted:\\s*(\\d{1,2}\\s+\\w{3}\\s+\\d{4})"
                        - name: dateparse
                          args: "2 Jan 2006"
                    # The site publishes no swarm counts on either the search or the detail page.
                    # The compiled indexer reports a flat 1, so match it rather than invent numbers.
                    seeders:
                      text: 1
                    leechers:
                      text: 0
                    downloadvolumefactor:
                      text: 0
                    uploadvolumefactor:
                      text: 1
                # WordPress 2.5

                """;
        }

        /// <summary>Results the patched indexer is expected to return for a broad query.</summary>
        public static int ProjectedResults(int pages) => Math.Clamp(pages, MinimumPages, MaximumPages) * ResultsPerPage;
    }
}
