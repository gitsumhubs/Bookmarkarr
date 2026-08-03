/*
 * Bookmarkarr - unified audiobook and ebook management
 * Copyright (C) 2026 Bookmarkarr Contributors
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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bookmarkarr.Infrastructure.Persistence
{
    /// <summary>
    /// Gives a fresh install working audiobook and ebook defaults without any manual
    /// database work, and fills in only what is missing on an existing install.
    /// </summary>
    /// <remarks>
    /// Every step is additive and idempotent. Nothing here renames, repoints, or
    /// overwrites a record the user created, and no credentials, hostnames, or secrets
    /// are ever seeded. Paths come from the conventional container mounts and can be
    /// overridden per deployment through environment variables.
    /// </remarks>
    public sealed class MediaDefaultsSeeder : IHostedService
    {
        private readonly IServiceProvider _provider;
        private readonly ILogger<MediaDefaultsSeeder> _logger;

        public MediaDefaultsSeeder(IServiceProvider provider, ILogger<MediaDefaultsSeeder> logger)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        private static string AudiobookRoot =>
            Environment.GetEnvironmentVariable("BOOKMARKARR_AUDIOBOOK_ROOT") ?? "/audiobooks";

        private static string EbookRoot =>
            Environment.GetEnvironmentVariable("BOOKMARKARR_EBOOK_ROOT") ?? "/ebooks";

        private static string DownloadsRoot =>
            Environment.GetEnvironmentVariable("BOOKMARKARR_DOWNLOADS_ROOT") ?? "/downloads";

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _provider.CreateScope();
                var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BookmarkarrDbContext>>();
                await using var db = await factory.CreateDbContextAsync(cancellationToken);

                // One transaction so a fresh install either gets a complete set of
                // defaults or none at all, never a half-seeded configuration.
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

                await SeedQualityProfilesAsync(db, cancellationToken);
                await SeedRootFoldersAsync(db, cancellationToken);

                // Flush before repairing: the repair pass queries the defaults that were
                // just added, and they need real keys before editions can point at them.
                await db.SaveChangesAsync(cancellationToken);

                await RepairEditionDefaultsAsync(db, cancellationToken);

                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                EnsureStagingDirectories();

                _logger.LogInformation("MediaDefaultsSeeder: media defaults verified.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Shutting down mid-seed is not an error; the next start repeats the pass.
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                // Seeding is a convenience. A failure here must never stop the app from
                // starting, because the user can still configure everything by hand.
                _logger.LogError(ex, "MediaDefaultsSeeder: failed to seed media defaults; continuing startup");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        /// <summary>
        /// Creates a default profile for each media type that does not already have one.
        /// </summary>
        private async Task SeedQualityProfilesAsync(BookmarkarrDbContext db, CancellationToken ct)
        {
            foreach (var mediaType in new[] { EditionMediaType.Audiobook, EditionMediaType.Ebook })
            {
                var hasDefault = await db.QualityProfiles
                    .AnyAsync(profile => profile.IsDefault && profile.MediaType == mediaType, ct);
                if (hasDefault)
                {
                    continue;
                }

                // An existing profile for this media type is promoted rather than
                // duplicated, so a user who made their own profile keeps it.
                var existing = await db.QualityProfiles
                    .FirstOrDefaultAsync(profile => profile.MediaType == mediaType, ct);
                if (existing is not null)
                {
                    existing.IsDefault = true;
                    existing.UpdatedAt = DateTime.UtcNow;
                    _logger.LogInformation(
                        "MediaDefaultsSeeder: promoted existing {MediaType} profile '{Name}' to default",
                        mediaType, existing.Name);
                    continue;
                }

                var seeded = mediaType == EditionMediaType.Audiobook
                    ? BuildDefaultAudiobookProfile()
                    : BuildDefaultEbookProfile();
                db.QualityProfiles.Add(seeded);
                _logger.LogInformation(
                    "MediaDefaultsSeeder: created default {MediaType} profile '{Name}'", mediaType, seeded.Name);
            }
        }

        private static QualityProfile BuildDefaultAudiobookProfile() => new()
        {
            Name = "Default Audiobook",
            Description = "Baseline audiobook profile created automatically on first run.",
            MediaType = EditionMediaType.Audiobook,
            IsDefault = true,
            // M4B first because it is the native audiobook container; the rest are
            // common fallbacks. Deliberately permissive on size and age so a new
            // install is not silently rejecting perfectly good releases.
            PreferredFormats = ["m4b", "mp3", "m4a", "flac", "opus", "ogg"],
            PreferredLanguages = ["English"],
            MinimumSize = 0,
            MaximumSize = 0,
            MaximumAge = 0,
            MinimumSeeders = 1,
            MinimumScore = 0,
            Qualities =
            [
                new QualityDefinition { Quality = "AAC 320kbps", Allowed = true, Priority = 0 },
                new QualityDefinition { Quality = "AAC 256kbps", Allowed = true, Priority = 1 },
                new QualityDefinition { Quality = "AAC 192kbps", Allowed = true, Priority = 2 },
                new QualityDefinition { Quality = "AAC 128kbps", Allowed = true, Priority = 3 },
                new QualityDefinition { Quality = "AAC 64kbps", Allowed = true, Priority = 4 },
                new QualityDefinition { Quality = "MP3 320kbps", Allowed = true, Priority = 5 },
                new QualityDefinition { Quality = "MP3 256kbps", Allowed = true, Priority = 6 },
                new QualityDefinition { Quality = "MP3 VBR", Allowed = true, Priority = 7 },
                new QualityDefinition { Quality = "MP3 192kbps", Allowed = true, Priority = 8 },
                new QualityDefinition { Quality = "MP3 128kbps", Allowed = true, Priority = 9 },
                new QualityDefinition { Quality = "MP3 64kbps", Allowed = true, Priority = 10 }
            ]
        };

        private static QualityProfile BuildDefaultEbookProfile() => new()
        {
            Name = "Default Ebook",
            Description = "Baseline ebook profile created automatically on first run.",
            MediaType = EditionMediaType.Ebook,
            IsDefault = true,
            // Ebook quality is a matter of format, not codec or bitrate. EPUB is the
            // most portable, then AZW3/MOBI for Kindle, with PDF last because it is
            // the least reflowable.
            PreferredFormats = ["epub", "azw3", "mobi", "pdf"],
            PreferredLanguages = ["English"],
            MinimumSize = 0,
            MaximumSize = 0,
            MaximumAge = 0,
            MinimumSeeders = 1,
            MinimumScore = 0,
            Qualities =
            [
                new QualityDefinition { Quality = "EPUB", Allowed = true, Priority = 0 },
                new QualityDefinition { Quality = "AZW3", Allowed = true, Priority = 1 },
                new QualityDefinition { Quality = "MOBI", Allowed = true, Priority = 2 },
                new QualityDefinition { Quality = "PDF", Allowed = true, Priority = 3 }
            ]
        };

        /// <summary>
        /// Registers the conventional library roots when their mounts are actually usable.
        /// </summary>
        private async Task SeedRootFoldersAsync(BookmarkarrDbContext db, CancellationToken ct)
        {
            foreach (var (mediaType, path, name) in new[]
            {
                (EditionMediaType.Audiobook, AudiobookRoot, "Audiobooks"),
                (EditionMediaType.Ebook, EbookRoot, "Ebooks")
            })
            {
                var existingForMedia = await db.RootFolders
                    .Where(root => root.MediaType == mediaType)
                    .ToListAsync(ct);

                if (existingForMedia.Count > 0)
                {
                    // Respect the user's library layout; only make sure something is
                    // marked default so adds for this media type have a destination.
                    if (!existingForMedia.Any(root => root.IsDefault))
                    {
                        existingForMedia[0].IsDefault = true;
                        existingForMedia[0].UpdatedAt = DateTime.UtcNow;
                        _logger.LogInformation(
                            "MediaDefaultsSeeder: marked existing {MediaType} root '{Path}' as default",
                            mediaType, existingForMedia[0].Path);
                    }
                    continue;
                }

                // Never register a root we cannot actually write into: doing so would
                // let imports fail later with a confusing error, or write into the
                // container's own filesystem instead of a mounted volume.
                if (!IsUsableDirectory(path))
                {
                    _logger.LogWarning(
                        "MediaDefaultsSeeder: {MediaType} root '{Path}' is missing or not writable; " +
                        "skipping automatic registration. Mount it and restart, or add a root folder manually.",
                        mediaType, path);
                    continue;
                }

                // A path already registered under the other media type is left alone
                // rather than duplicated under a second row.
                if (await db.RootFolders.AnyAsync(root => root.Path == path, ct))
                {
                    continue;
                }

                db.RootFolders.Add(new RootFolder
                {
                    Name = name,
                    Path = path,
                    MediaType = mediaType,
                    IsDefault = true
                });
                _logger.LogInformation(
                    "MediaDefaultsSeeder: registered {MediaType} root '{Path}'", mediaType, path);
            }
        }

        /// <summary>
        /// Backfills editions that were created before media-aware defaults existed.
        /// </summary>
        private async Task RepairEditionDefaultsAsync(BookmarkarrDbContext db, CancellationToken ct)
        {
            var editions = await db.BookEditions
                .Where(edition => edition.QualityProfileId == null || edition.RootFolderId == null)
                .ToListAsync(ct);
            if (editions.Count == 0)
            {
                return;
            }

            var profiles = await db.QualityProfiles
                .Where(profile => profile.IsDefault)
                .ToDictionaryAsync(profile => profile.MediaType, ct);
            var roots = await db.RootFolders
                .Where(root => root.IsDefault)
                .ToDictionaryAsync(root => root.MediaType, ct);

            var repaired = 0;
            foreach (var edition in editions)
            {
                // Only ever fills a gap. An edition that already points somewhere keeps
                // pointing there, including deliberate non-default choices.
                if (edition.QualityProfileId is null
                    && profiles.TryGetValue(edition.MediaType, out var profile))
                {
                    edition.QualityProfileId = profile.Id;
                    repaired++;
                }

                if (edition.RootFolderId is null
                    && roots.TryGetValue(edition.MediaType, out var root))
                {
                    edition.RootFolderId = root.Id;
                    if (string.IsNullOrWhiteSpace(edition.RootPath))
                    {
                        edition.RootPath = root.Path;
                    }
                    repaired++;
                }
            }

            if (repaired > 0)
            {
                _logger.LogInformation(
                    "MediaDefaultsSeeder: filled {Count} missing profile/root assignment(s) on existing editions",
                    repaired);
            }
        }

        /// <summary>
        /// Creates the conventional per-media staging directories under the download mount.
        /// </summary>
        private void EnsureStagingDirectories()
        {
            var downloadsRoot = DownloadsRoot;

            // Absent mount: warn rather than create, so we never write into the image
            // layer and give the user a filled-looking directory that vanishes on upgrade.
            if (!Directory.Exists(downloadsRoot))
            {
                _logger.LogWarning(
                    "MediaDefaultsSeeder: download root '{Path}' is not mounted; " +
                    "per-media staging directories were not created. Grabs will fail until it is mounted.",
                    downloadsRoot);
                return;
            }

            foreach (var mediaFolder in new[] { "audiobooks", "ebooks" })
            {
                var path = Path.Combine(downloadsRoot, mediaFolder);
                try
                {
                    if (!Directory.Exists(path))
                    {
                        Directory.CreateDirectory(path);
                        _logger.LogInformation("MediaDefaultsSeeder: created staging directory '{Path}'", path);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(ex,
                        "MediaDefaultsSeeder: could not create staging directory '{Path}'. " +
                        "Check the mount's permissions; grabs for this media type may fail.",
                        path);
                }
            }
        }

        /// <summary>
        /// Returns true when the directory exists and this process can write into it.
        /// </summary>
        private static bool IsUsableDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return false;
            }

            // Existence is not enough: a read-only bind mount looks identical until the
            // first write fails, so probe with a temp file we immediately remove.
            try
            {
                var probe = Path.Combine(path, $".bookmarkarr-write-probe-{Guid.NewGuid():N}");
                using (File.Create(probe)) { }
                File.Delete(probe);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}
