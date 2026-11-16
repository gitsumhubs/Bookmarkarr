# Releases and GHCR

## Continuous Integration

`.github/workflows/ci.yml` restores locked .NET dependencies, installs Node 24 dependencies, type-checks and unit-tests the frontend, runs backend tests, runs migration tests, validates Compose, and checks the Dockerfile build definition.

## Version Releases

Pushing a semantic version tag such as `v1.3.0` to GitHub triggers `.github/workflows/publish-ghcr.yml`. It builds and publishes `linux/amd64` and `linux/arm64` through Buildx.

Generated GHCR tags are:

- Exact semantic version, such as `1.3.0`
- Minor line, such as `1.3`
- Major line, such as `1`
- `latest`

The image name is `ghcr.io/<repository-owner>/bookmarkarr`. GitHub’s workflow token receives only `contents: read` and `packages: write`.

## Public Repository

The public source repository is `https://github.com/gitsumhubs/Bookmarkarr`. Push a semantic version tag to publish the matching GHCR image, then configure package visibility in GitHub if anonymous pulls are intended.

## Local Release Validation

```bash
docker build -t bookmarkarr:release-candidate .
docker run --rm --entrypoint dotnet bookmarkarr:release-candidate --info
```

Also run all commands in the README development/testing section and verify a clean application startup against a fresh config directory before tagging.

## Edition monitoring upgrade

Starting with 0.1.44, automatic search selects editions independently. Older installations may
have audiobook edition monitoring flags that disagree with the legacy parent book flags. To
preserve the previously effective audiobook monitoring choices, stop the application, back up the
config volume and Compose definition, and preview the alignment against the database:

```bash
python3 scripts/reconcile-edition-monitoring.py --database /path/to/config/database/bookmarkarr.db
python3 scripts/reconcile-edition-monitoring.py --database /path/to/config/database/bookmarkarr.db \
  --apply --backup-dir /path/to/backups/before-0.1.44
```

Apply only after reviewing the preview. The script also makes an SQLite backup and records the
before/after flags. It changes audiobook monitoring flags only; ebook monitoring, paths, profiles,
and categories stay as configured. Startup seeding remains additive and does not perform this
alignment automatically.

## 0.1.46 behaviour changes

0.1.46 includes a database migration. Back up the config volume before upgrading; the migration's
rollback does not restore the settings below.

- **Upgrades off for every edition.** The migration sets `UpgradeAllowed` to off on all existing
  editions, and new editions default to off. A book that already has files is no longer searched
  again for a better release. Turn upgrades back on per edition where you want them.
- **Downloaded books stop being monitored.** "Stop Monitoring Imported Books" (Settings > Search)
  is now on by default, and the migration turns it on for existing installs. An edition is
  unmonitored once its import succeeds and it has files. Turn the setting off to keep monitoring.
- **Unattended grabs are checked against the wanted book.** Automatic and Goodreads grabs refuse
  releases whose title, author or series number do not match, packs and collections, and
  releases outside the size limits. Manual picks from Interactive Search are unaffected. Release
  language is still only a scoring penalty in 0.1.46.
- **Indexer refusals stop the search.** A 429/403/503 or connection failure puts that indexer on an
  escalating cooldown instead of retrying it every pass. AudioBook Bay is charged per page.
- **Packs are held at import.** A download that looks like several books is held in Import Blocked;
  retry it to import as one book, or use Manual Import to place the books separately.
- **New installs only:** the completed-download action defaults to hardlink-or-copy, and new usenet
  clients default to removing the job and its files after import. Existing settings are unchanged.

## Updating a running deployment

```bash
docker compose pull
docker compose up -d
curl --fail http://localhost:3018/api/v1/system/ready
curl --fail http://localhost:3018/api/v1/system/info
```

The published image includes `org.opencontainers.image.version` and
`org.opencontainers.image.revision`. Compare the revision with System Info to identify the running
source. Manual image builds can set `--build-arg VERSION=0.1.46 --build-arg VCS_REF=<commit>`.

The .NET SDK and Go compiler run natively on the build host and target the requested Docker
architecture. The final amd64/arm64 runtime image uses the matching target dependencies. This uses
[Microsoft's recommended cross-compilation pattern](https://devblogs.microsoft.com/dotnet/improving-multiplatform-container-support/).
