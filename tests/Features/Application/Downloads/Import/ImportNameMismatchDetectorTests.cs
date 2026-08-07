/*
 * Bookmarkarr - Audiobook Management System
 * Copyright (C) 2024-2026 Bookmarkarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

namespace Bookmarkarr.Tests.Features.Application.Downloads.Import
{
    public sealed class ImportNameMismatchDetectorTests
    {
        private static string P(params string[] parts) => Path.Combine(parts);

        [Fact]
        public void Detect_PairsAFileWhoseDotsTheClientStripped()
        {
            // The live case: RDT wrote "Ash and Elm....mp3" as "Ash and Elmmp3", losing even the
            // extension, then kept reporting the original name. The import looked for a file that
            // would never exist and retried until it blocked.
            var dir = P("downloads", "The Name of the Wind");
            var missing = new[] { P(dir, "01 Ch82_ Ash and Elm....mp3") };
            var onDisk = new[] { P(dir, "01 Ch82_ Ash and Elmmp3"), P(dir, "01 Ch01_ A Place for Demons.mp3") };

            var matches = ImportNameMismatchDetector.Detect(missing, [P(dir, "01 Ch01_ A Place for Demons.mp3")], onDisk);

            var match = Assert.Single(matches);
            Assert.Equal(P(dir, "01 Ch82_ Ash and Elm....mp3"), match.ReportedPath);
            Assert.Equal(P(dir, "01 Ch82_ Ash and Elmmp3"), match.ActualPath);
        }

        [Fact]
        public void Detect_PairsAFileWhoseApostropheTheClientSpacedOut()
        {
            var dir = P("downloads", "Discount Dan");
            var missing = new[] { P(dir, "Discount Dan's.m4b") };
            var onDisk = new[] { P(dir, "Discount Dan s.m4b") };

            var match = Assert.Single(ImportNameMismatchDetector.Detect(missing, [], onDisk));
            Assert.Equal(P(dir, "Discount Dan s.m4b"), match.ActualPath);
        }

        [Fact]
        public void Detect_RefusesToGuessWhenTwoCandidatesReduceTheSame()
        {
            // Importing the wrong audio under a book's name is near-impossible to notice later,
            // so an ambiguous pairing must be left for a person rather than picked.
            var dir = P("downloads", "Ambiguous");
            var missing = new[] { P(dir, "Chapter 1.mp3") };
            var onDisk = new[] { P(dir, "Chapter1.mp3"), P(dir, "Chapter_1.mp3") };

            Assert.Empty(ImportNameMismatchDetector.Detect(missing, [], onDisk));
        }

        [Fact]
        public void Detect_NeverOffersAFileTheClientAlreadyAccountedFor()
        {
            // A file matched to another reported entry is spoken for; reusing it would import one
            // payload twice and leave the real file behind.
            var dir = P("downloads", "Claimed");
            var missing = new[] { P(dir, "Chapter 2.mp3") };
            var present = new[] { P(dir, "Chapter2.mp3") };

            Assert.Empty(ImportNameMismatchDetector.Detect(missing, present, present));
        }

        [Fact]
        public void Detect_IgnoresCandidatesInOtherDirectories()
        {
            // A same-named file from an unrelated release must never be pulled in.
            var missing = new[] { P("downloads", "Book A", "Chapter 1.mp3") };
            var onDisk = new[] { P("downloads", "Book B", "Chapter1.mp3") };

            Assert.Empty(ImportNameMismatchDetector.Detect(missing, [], onDisk));
        }

        [Fact]
        public void Detect_ReturnsNothingWhenThereIsNoMissingFile()
        {
            Assert.Empty(ImportNameMismatchDetector.Detect([], [], [P("downloads", "a.mp3")]));
        }
    }
}
