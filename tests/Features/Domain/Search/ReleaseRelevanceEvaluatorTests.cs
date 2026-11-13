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

namespace Bookmarkarr.Tests.Features.Domain.Search
{
    public class ReleaseRelevanceEvaluatorTests
    {
        private static ReleaseRelevanceVerdict Evaluate(string title, string author, string release, string? series = null, string? number = null) =>
            ReleaseRelevanceEvaluator.Evaluate(
                new WantedBook { Title = title, Authors = [author], Series = series, SeriesNumber = number }, release);

        // The same book under the spellings indexers and usenet posters actually use.
        [Theory]
        [InlineData("The Way of Kings (The Stormlight Archive, #1)", "Brandon Sanderson", "The Way of Kings: The Stormlight Archive, Book 1 - Brandon Sanderson [M4B] [64 Kbps]")]
        [InlineData("The Way of Kings (The Stormlight Archive, #1)", "Brandon Sanderson", "Brandon.Sanderson-Stormlight.Archive.01-The.Way.of.Kings.2010.[AUDIOBOOK]")]
        [InlineData("Oathbringer (The Stormlight Archive, #3)", "Brandon Sanderson", "The Stormlight Archive 3: Oathbringer - Brandon Sanderson [M4B]")]
        [InlineData("Edgedancer (The Stormlight Archive, #2.5)", "Brandon Sanderson", "Brandon.Sanderson-The.Stormlight.Archive.Book.2.5-Edgedancer.2022.[AUDIOBOOK]")]
        [InlineData("Edgedancer (The Stormlight Archive, #2.5)", "Brandon Sanderson", "The.Stormlight.Archive.[Edgedancer]")]
        [InlineData("Mistborn: The Final Empire (Mistborn, #1)", "Brandon Sanderson", "Mistborn [01] The Final Empire [Parts 1 ~ 2 ~3] - Brandon Sanderson [MP3] [256 Kbps]")]
        [InlineData("The Black Prism (Lightbringer, #1)", "Brent Weeks", "The Lightbringer Saga Book 1 - The Black Prism - Brent Weeks [M4B] [64 Kbps]")]
        [InlineData("The Crown Tower (The Riyria Chronicles, #1)", "Michael J. Sullivan", "Riyria Chronicles [1] The Crown Tower - Michael J. Sullivan [MP3] [256 Kbps]")]
        [InlineData("Night Watch (Discworld, #29; City Watch, #6)", "Terry Pratchett", "Night Watch (#29), 2023 Penguin Audio - Terry Pratchett [M4B] [128 Kbps]")]
        [InlineData("Night Watch (Discworld, #29; City Watch, #6)", "Terry Pratchett", "Discworld-Terry.Pratchett.29-Night.Watch.-AudioBook")]
        [InlineData("Harry Potter and the Sorcerer’s Stone (Full-Cast Edition)", "J.K. Rowling", "Harry Potter and the Sorcerers Stone [Full-Cast Edition] EAC3-Atmos 6ch-J.K")]
        [InlineData("Harry Potter and the Philosopher’s Stone (Harry Potter, #1)", "J.K. Rowling", "JK Rowling - Harry Potter, Bk 1 - The Philosophers Stone (NMR")]
        [InlineData("A Darker Shade of Magic (Shades of Magic, #1)", "V.E. Schwab", "A Darker Shade of Magic (Shade of Magic #1) - V.E Schwab [MP3]")]
        [InlineData("World War Z: An Oral History of the Zombie War", "Max Brooks", "World War Z An Oral History of the Zombie War - Max Brooks [M4B] [128 Kbps]")]
        [InlineData("Good Omens: The Nice and Accurate Prophecies of Agnes Nutter, Witch", "Terry Pratchett", "Good.Omens-Neil.Gaiman.Terry.Pratchett")]
        [InlineData("China", "Edward Rutherfurd", "req.China.by.Edward.Rutherfurdnovel.spanning.1839.to.present.m4b")]
        [InlineData("The Lightning Thief (Percy Jackson and the Olympians, #1)", "Rick Riordan", "DLing Dying HD NMR Thanks to OPs-Rick Riordan - The Lightning Thief")]
        [InlineData("The Sea of Monsters (Percy Jackson and the Olympians, #2)", "Rick Riordan", "The Sea of Monsters (Percy Jackson and the Olympians, Book 2)")]
        [InlineData("The Sandman: Act II (Sandman Audible Original, #2)", "Dirk Maggs", "The.Sandman-Act.II.by.Neil.Gaiman.Dirk.Maggs")]
        [InlineData("He Who Fights with Monsters (He Who Fights with Monsters, #1)", "Shirtaloon", "Shirtaloon-2021-He.Who.Fights.with.Monsters.1.Fantasy-[AUDIOBOOK]")]
        [InlineData("Promise of Blood (Powder Mage, #1)", "Brian  McClellan", "Promise of Blood (The Powder Mage Trilogy) - Brian McClellan [MP3]")]
        public void Accepts_TheWantedBook(string title, string author, string release)
        {
            var verdict = Evaluate(title, author, release);
            Assert.True(verdict.IsRelevant, verdict.Detail);
        }

        // Every release here was grabbed for the book beside it, or would have been.
        [Theory]
        [InlineData("The Titan's Curse", "Rick Riordan", "Entire Audiobooks Collection - Various [M4B]", ReleaseRejection.MultiBookPack)]
        [InlineData("Dragonflight (Dragonriders of Pern, #1)", "Anne McCaffrey", "Anne McCaffrey Collection - Anne McCaffrey [M4B] [Variable]", ReleaseRejection.MultiBookPack)]
        [InlineData("He Who Fights with Monsters (He Who Fights with Monsters, #1)", "Shirtaloon", "He Who Fights With Monsters, Books 1-12 - Shirtaloon [Mixed] [Mixed]", ReleaseRejection.MultiBookPack)]
        [InlineData("Going Postal (Discworld, #33; Moist von Lipwig, #1)", "Terry Pratchett", "Discworld Moist Von Lipwig Collection: Going Postal, Making Money, Raising Steam - Terry Pratchett [M4B]", ReleaseRejection.MultiBookPack)]
        [InlineData("Ready Player One (Ready Player One, #1)", "Ernest Cline", "Ready Player One and Two - Ernest Cline [M4B] [64 Kbps]", ReleaseRejection.MultiBookPack)]
        [InlineData("Ready Player One (Ready Player One, #1)", "Ernest Cline", "Ready Player Two (Ready Player One #2) - Ernest Cline [MP3] [64 Kbps]", ReleaseRejection.DifferentVolume)]
        [InlineData("Dungeon Crawler Carl (Dungeon Crawler Carl, #1)", "Matt Dinniman", "Dungeon Crawler Carl - Season 3 (Book 3) Episodes 1-10 [Chapterised] - Audio Immersion Tunnel - Matt Dinniman [M4B]", ReleaseRejection.DifferentVolume)]
        [InlineData("Dungeon Crawler Carl (Dungeon Crawler Carl, #1)", "Matt Dinniman", "This Inevitable Ruin: Dungeon Crawler Carl, Book 7 - Matt Dinniman [M4B] [64 Kbps]", ReleaseRejection.DifferentVolume)]
        [InlineData("He Who Fights with Monsters (He Who Fights with Monsters, #1)", "Shirtaloon", "He.Who.Fights.with.Monsters.10.[B0C1PV6Q7C]", ReleaseRejection.DifferentVolume)]
        [InlineData("QualityLand (QualityLand, #1)", "Marc-Uwe Kling", "QualityLand 2.0 Kikis Geheimnis - Marc-Uwe Kling [M4A]", ReleaseRejection.DifferentVolume)]
        [InlineData("Path of the Berserker: A Daopocalypse Progression Fantasy", "Rick Scott", "Path of the Berserker 4: A Daopocalypse Progression Fantasy - Rick Scott [M4B]", ReleaseRejection.DifferentVolume)]
        [InlineData("The Sandman", "Dirk Maggs", "The Sandman: Act III - Neil Gaiman, Dirk Maggs [MP3]", ReleaseRejection.DifferentVolume)]
        [InlineData("The Sandman: Act II (Sandman Audible Original, #2)", "Dirk Maggs", "Neil.Gaiman.Dirk.Maggs-The.Sandman", ReleaseRejection.DifferentVolume)]
        [InlineData("Eragon (The Inheritance Cycle #1)", "Christopher Paolini", "Christopher Paolini - Eragon - Book 2 - Eldest (NMR", ReleaseRejection.DifferentVolume)]
        [InlineData("Assassin's Apprentice (Farseer Trilogy, #1)", "Robin Hobb", "Reborn as an Assassins Apprentice, Vol. 1 - okiuta [M4B] [128 Kbps]", ReleaseRejection.DifferentTitle)]
        [InlineData("Eragon (The Inheritance Cycle #1)", "Christopher Paolini", "05.Murtagh.The.World.of.Eragon", ReleaseRejection.DifferentTitle)]
        [InlineData("Dragonflight (Dragonriders of Pern, #1)", "Anne McCaffrey", "War of the Scaleborn (World of Warcraft: Dragonflight) - Courtney Alameda [M4B]", ReleaseRejection.DifferentAuthor)]
        [InlineData("Age of Myth (The Legends of the First Empire, #1)", "Michael J. Sullivan", "Legends of the First Empire 1: Age of Myth (1 of 2) - Michael J. Sullivan [MP3] [256 Kbps]", ReleaseRejection.PartialRelease)]
        [InlineData("Ship of Magic (Liveship Traders, #1)", "Robin Hobb", "Liveship Traders Trilogy [chapterized] - Robin Hobb [MP3]", ReleaseRejection.TitleNotFound)]
        [InlineData("Eragon (The Inheritance Cycle #1)", "Christopher Paolini", "The.Dragon.Reborn.Book.Three.of.The.Wheel.of.Time.by.Robert.Jordan", ReleaseRejection.TitleNotFound)]
        [InlineData("The Silmarillion", "J.R.R. Tolkien", "The.Silmarillion", ReleaseRejection.AuthorUnconfirmed)]
        public void Rejects_AnotherBook(string title, string author, string release, ReleaseRejection expected)
        {
            var verdict = Evaluate(title, author, release);
            Assert.False(verdict.IsRelevant, verdict.Detail);
            Assert.Equal(expected, verdict.Rejection);
        }

        [Fact]
        public void ReadsSeriesAndVolume_FromTheSeparateFields()
        {
            Assert.True(Evaluate("The City of Akul", "Macronomicon", "The City of Akul The Legend of William Oh, Book 2 - Macronomicon [M4B]", "The Legend of William Oh", "2").IsRelevant);

            var first = Evaluate("The Legend of William Oh", "Macronomicon", "The City of Akul The Legend of William Oh, Book 2 - Macronomicon [M4B]", "The Legend of William Oh", "1");
            Assert.Equal(ReleaseRejection.DifferentVolume, first.Rejection);
        }

        [Fact]
        public void PackWordsTheBookUsesItself_AreNotHeldAgainstIt()
        {
            var verdict = Evaluate("The Complete Sherlock Holmes Collection", "Arthur Conan Doyle", "The Complete Sherlock Holmes Collection - Arthur Conan Doyle [M4B]");
            Assert.True(verdict.IsRelevant, verdict.Detail);
        }

        [Fact]
        public void WithoutATitle_NothingIsAccepted()
        {
            var verdict = ReleaseRelevanceEvaluator.Evaluate(new WantedBook { Title = " " }, "Anything - Anyone [M4B]");
            Assert.Equal(ReleaseRejection.TitleNotFound, verdict.Rejection);
        }
    }
}
