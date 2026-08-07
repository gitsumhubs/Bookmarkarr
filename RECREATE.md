# Bookmarkarr

## Overview

Bookmarkarr is a standalone full-source Listenarr 1.2.2 fork for managing audiobook and ebook editions independently under a unified book. It uses external indexers and download clients and ships as one application container. The current bulk library-import workflow is audiobook-focused; a Radarr-style scan, match-review, and import workflow for existing ebook files remains planned future work.

Normal operators deploy from the public `ghcr.io/gitsumhubs/bookmarkarr:latest` image with the standalone root `docker-compose.yml`. The file contains no build configuration and requires neither a source checkout nor `.env`; environment-variable overrides and external Docker networks remain optional. This private working tree preserves the existing `BOOKMARKARR_IMAGE` override used by the hosted local-image instance, while the exported public Compose pins GHCR directly.

Library adds require either a configured root folder or an explicit destination. Empty and legacy application-directory output paths are never used for media storage. Download-client API compatibility maps a top-level category into `SettingsJson`, exposes category/download path on reads, accepts both POST upsert and `PUT /download-clients/{id}`, and preserves stored credentials when redacted placeholders are submitted. Goodreads commits are additive, create media-aware editions, record an audiobook's derived per-book `BasePath`, and only schedule searches when `searchAfterImport` is explicitly enabled. The Wanted view keeps active downloads visible through queued/downloading edition status plus queue-snapshot edition identity. Concurrent client queue polls resolve an isolated scoped gateway per client so EF Core contexts are not shared across parallel work.

Audiobook destination derivation is centralized in `AudiobookPathPlanner`. It combines the audiobook edition's registered root with `ApplicationSettings.FolderNamingPattern`, recognizes edition rows that already contain a full per-book path, and preserves any existing custom `Books.BasePath`. Goodreads creation, library previews/bulk edits, startup repair, and completed-download import all use this derivation. This prevents legacy books with an empty `BasePath` from leaving completed files in `Import Blocked`.

Completion requires that no bytes remain outstanding, not merely a terminal state label. Debrid-backed clients (RDT Client against AllDebrid and similar) report `completed` the moment the *remote* side finishes, which can be gigabytes before the files exist locally; `QueueItemConverter` therefore lets reliable byte telemetry overrule the label and holds the download at `Downloading` while `AmountLeft > 0`. Clients that expose no byte counts still honour the label, or they would never import. When an import job does exhaust its retries, `ScheduleRetryAsync` now moves the download to `ImportBlocked` — previously the job died while the row stayed at `ImportPending`, and because an import-pending row counts as an active download it silently suppressed automatic search for that book forever. Retry backoff runs 2/4/8/16/32 minutes across a default budget of five attempts, roughly an hour; the previous 0.5/1/2/4 schedule gave up after about seven minutes, far less than a large transfer needs to land.

`DELETE /api/v1/download/queue/{downloadId}?blocklist=true` blocklists the release before removing it, mirroring Sonarr's `DELETE /api/v3/queue/{id}?blocklist=true` so an external cleanup daemon can drive Bookmarkarr the way it drives the other *arrs. It authenticates with `X-Api-Key`. The download is resolved from any of its identifiers — Bookmarkarr id, client id, or torrent hash — so a caller holding only a hash needs no queue lookup first. This path blocklists on the first request rather than counting strikes: a caller that has already watched a torrent sit at zero bytes across several checks knows more than a single status poll does. The response reports `blocklisted` separately from removal, because a torrent client answers a delete for an unknown hash with success and removal alone therefore cannot tell a caller that Bookmarkarr recognised the release. `rdt-cleanup` consumes this as a third target beside Sonarr and Radarr, configured under a `bookmarkarr` key and skipped entirely when unset.

Releases that repeatedly fail to download are blocklisted per book. `BlocklistService` counts failures of the same release from the `Downloads` rows themselves — no separate counter to fall out of sync — and writes a `BlocklistEntries` row on the second failure, because one failure is routine (a stalled peer, a transient client error) and blocking on it would discard releases that succeed on a plain retry. Only `Failed` counts: `ImportBlocked` means the bytes arrived and Bookmarkarr could not file them, so blocklisting there would discard a good release over a fault on our side. Release identity is the indexer GUID when both sides carry one, otherwise a separator-and-case-normalized release name plus size within one percent; `ReleaseIdentity.NormalizeReleaseName` deliberately does *not* use `TitleUtils.NormalizeTitle`, which strips the quality, format, and group tags that distinguish one release of a book from another. Scope is one book, never global, so a bad post cannot ban an indexer. Automatic search skips blocklisted releases and falls through to the next best candidate, and a blocklisting clears the book's `LastSearchTime` so the next cycle re-searches it rather than waiting out its six-hour window; the process terminates because each pass either grabs a different release or reports nothing left to try. `GET/DELETE /api/v1/blocklist` lists and removes entries, and `DELETE /api/v1/blocklist/audiobook/{id}` clears a book so a retry starts clean. A Blocklist page lists every entry with the release, book, indexer, failure count, and reason, and removes entries individually. `POST /api/v1/blocklist/check` reports which of a supplied set of releases are blocklisted for a book; manual search uses it to badge results rather than reimplementing `ReleaseIdentity` in the frontend, where the two would drift apart. Blocklisted results are marked, never hidden — a manual grab may know something the failure history does not. The lookup runs after scoring in its own try/catch, so losing the badge can never cost the user their quality scores.

Source-file discovery treats an extension-shaped content directory as a single-file layout only for torrent clients. A usenet release whose *name* ends in a media extension makes NZBGet create a destination directory indistinguishable from the qBittorrent single-file layout, but its payload sits under the unpacked release folder instead of being repeated as a same-named file; refusing to recurse there stranded complete downloads in `ImportBlocked`. The distinction comes from the resolved adapter's declared `DownloadProtocol`, not a hardcoded list of client type names, so a new usenet client is covered automatically. Directory expansion also drops files beneath a client working directory (`_unpack`, `_failed`), because NZBGet leaves its unpack copy in place and importing both stores the book twice.

`POST /api/v1/library/adopt?dryRun=true` plans adoption of library folders Bookmarkarr has no record of, and `dryRun=false` commits the reviewed plan. It exists because the database is a far smaller picture than the disk — hundreds of folders can predate the install — and a book Bookmarkarr does not know about is downloaded again the moment it is added. `LibraryAdoptionWorkflow` walks the root, keeps only the deepest folders that directly contain audio (so a book split into disc subfolders is not adopted as several books, and an author folder does not swallow everything beneath it), then identifies each one.

Identification prefers Audiobookshelf, which has already matched these exact folders against Audible and holds an ASIN — an exact key where title matching is guesswork. `AudiobookshelfUrl` and `AudiobookshelfApiKey` are optional: an unreachable or unconfigured server degrades to folder-name parsing rather than failing the scan, because Audiobookshelf is an accelerator and never a dependency. It is also never authoritative on its own, since it scans on a schedule and a freshly imported book may not appear yet; disk decides what exists, Audiobookshelf only says what it is.

Audiobookshelf's grouping also overrides the walk's own folder boundaries. A book stored as `Disc 1`/`Disc 2` is discovered as two candidates by a deepest-folder walk and would be adopted as two books; Audiobookshelf has already resolved it to one item, so folders beneath a known item are collapsed onto it and their file counts merged. Folders it knows nothing about are left exactly as discovered.

Only an Audiobookshelf match is trusted enough to create a book from. A folder name is a guess — the same tree routinely mixes `Author/Title`, `Series/Title`, publisher-as-author, and bare titles, and no rule reads all of them correctly — so folder-derived identities are reported as `NeedsReview` and left untouched. Adopted books are created unmonitored, because switching monitoring on for an entire adopted library at once would put all of it into the search rotation immediately. Existing books are matched by ASIN first and by normalized title plus author second.

Automatic search walks a ladder of progressively broader queries and stops at the first rung returning results: precise title plus author, then bracketed/series decoration stripped, then the subtitle after a colon dropped, then each of those without the author, then the title with apostrophe-bearing words removed plus the author, and finally the single most distinctive title word plus the author. The subtitle rung refuses to shorten to something too generic — a main title must be two words or eight characters, so `It: A Novel` is never reduced to `It`. The apostrophe rung exists because Prowlarr's AudioBook Bay indexer searches with `&tt=1`, a title-only mode matching whole words against a title stored with a typographic apostrophe: `Butcher's` leaves Prowlarr as `butcher s`, and that form, the stripped `Butchers`, and the bare stem `Butcher` all return zero, while dropping the word finds the release immediately. The distinctive word is picked from the apostrophe-free form so an unmatchable word is never chosen, and both final rungs are skipped without an author, since a bare word is far too broad. It deliberately stops before querying by author alone, which returns unrelated books.

A manual grab supersedes an automatic one for the same book. `AutomaticGrabSupersedeWorkflow` removes any in-flight download for that book and media type that is not marked as a manual grab, then the manual send proceeds; without it the duplicate guard returned a 409 the operator never saw while the automatic download — frequently the wrong release, which is why they were grabbing by hand — kept running. Superseded releases are not blocklisted, since losing to a human's choice is not a failure. Manual grabs never displace other manual grabs. Downloads are marked manual on creation and anything unmarked reads as automatic, which is the safe direction because the alternative is refusing to replace a download the operator is actively trying to replace; only in-flight rows are considered, so the ambiguity for pre-existing rows is short-lived.

A successful import marks its edition `Imported` independently of whether that pass registered the file row. The two shared a guard, so an already-registered path skipped both and left the edition at `Downloading` with files on disk — still counting as wanted, so the book stayed in Wanted and remained eligible for another download. A collection grab additionally records its originating book as download metadata (never as `AudiobookId`, which would suppress that book's search while the collection ran) and marks that book's audiobook edition `Imported` on completion. That last step is deliberately unverified — the placed folders are untracked until a scan, so a collection lacking the book still marks it finished and it will not be searched again.

Manual search shares the automatic path's fallbacks. `IndexerSearchWorkflow.SearchIndexerResultsAsync` runs the typed query first and returns it whenever it finds anything, falling through `DownloadSearchQueryBuilder.BuildFreeTextCandidates` — decoration stripped, then apostrophe-bearing words dropped — only on an empty result. Without this the ladder existed solely on the automatic path, so searching a title by hand could return nothing for a book automatic search located immediately.

Byte telemetry overrules a completion label only when the client actually reports bytes. `QueueItemConverter` treats size/downloaded as trusted when the total is positive and the downloaded count is non-negative, but zero downloaded bytes alongside a completion claim — a terminal state or full progress — means the client does not populate byte counts at all rather than that nothing transferred. RDT Client's qBittorrent shim leaves `downloaded` at 0 for a torrent's whole life, so a finished file reads as entirely outstanding and the download would otherwise stay `Downloading` permanently, blocking its import and, because an active row suppresses automatic search, any further search for that book. The exemption is deliberately narrow: a transfer genuinely stalled at zero bytes reports neither a completed state nor full progress, so the debrid protection against importing before files land is unaffected.

The download monitor polls only what a poll can still change. `GetActiveAsync` deliberately includes `Moved` rows so deferred client removal keeps their `CanBeRemoved` metadata fresh, but a client whose `RemoveCompletedDownloads` is `none` never removes anything, and imported rows are terminal and accumulate for the life of the library — so polling them meant every completed download was re-queried on every cycle and cycle time grew without bound. `DownloadMonitorService` therefore drops imported rows belonging to clients that never remove, leaving the cycle proportional to what is genuinely in flight; clients configured to remove are unaffected. The Wanted view reloads the library and downloads every 15 seconds as well, skipping a tick rather than stacking requests and pausing while the tab is hidden: row membership comes from the library, which is otherwise kept current only by SignalR pushes, so a dropped connection previously froze the page with no indication.

Manual search offers a per-grab **Grab as collection** checkbox. A collection download is placed at the audiobook root by `CollectionPassthroughImporter` with its relative folder structure and filenames preserved, so Audiobookshelf reads the release's own `Author/Title` folders as separate items instead of one book with hundreds of tracks; the ordinary import is book-shaped and would flatten and rename every file into one book's folder. The row carries no `AudiobookId` on purpose, because an attached download makes `AutomaticSearchService` treat that book as having an active download and stop searching it; a `CollectionPassthrough` metadata marker distinguishes an intentionally book-less row from a corrupt one, which the processor rejects, so no schema change was required. `DownloadProcessingJobProcessor` branches to the passthrough before its audiobook guard, and every destination is verified to resolve inside the root before writing. No library rows are created — the placed books remain untracked until a library scan or adopt-scan reviews them, since folder-derived identity is exactly what adoption reports as `NeedsReview` rather than committing.

Imported files are renamed by the configured patterns unless renaming is switched off. `DisableFileRenaming` in application settings is the library-wide default, and `Books.DisableFileRenamingOverride` is a per-book nullable override where null inherits that default; the effective value is resolved once per import in `DownloadImportService`. When renaming is off the folder patterns still apply and only the leaf filename is preserved, and the incoming name is forced to a sanitized bare filename so a source name can never introduce a directory. Renaming is what makes multi-file releases sort correctly — players and Audiobookshelf order tracks by filename, and source names such as `part1`/`part10`/`part2` do not sort — so disabling it can scramble playback order for multi-part books while costing nothing for single-file ones. Both default to renaming enabled. The per-book value is set through `PUT /api/v1/library/{id}/file-renaming` rather than the audiobook `PUT`, which binds a whole entity and would treat an absent override as an intentional "inherit".

Search queries drop apostrophes rather than replacing them with a space. Spacing a word-internal mark turns `Carl's` into `Carl s`, and indexers that tokenize on whitespace never match `Carls` — automatic search returned zero results for books the manual path found immediately, because the two normalized punctuation differently. Genuine separators (colons, brackets, commas) still become spaces.

Automatic search asks the filesystem, not only the database, before grabbing. `LibraryPresenceInspector` resolves the book's destination through `AudiobookPathPlanner` and reports the audio files actually present there. Database rows are an incomplete picture of the library — files imported before a book was tracked, and files whose import failed after the bytes landed, have no `AudiobookFiles` row at all — so a row-only check reads those books as missing and grabs them a second time. On-disk quality is derived from file extension alone and therefore rounds down to the worst matching profile rung, which lets a genuinely better release still register as an upgrade; the check suppresses duplicates without ever suppressing an upgrade. When files exist but match no rung in the profile there is nothing to compare a candidate against, so the search is skipped with a warning directing the operator to run a library scan. A path that collapses to a library root is ignored rather than scanned, since that would report every book in the library as already present. Unreadable directories report absence so search behaves exactly as it did before the check existed. The inspector is audio-only because automatic search runs per audiobook against an audio quality profile.

Library status reconciliation is explicit and dry-run-first at authenticated `POST /api/v1/library/status/reconcile?dryRun=true`. It compares edition file registrations and tracked blocked downloads with fresh per-client queue snapshots. Completed, ready, and import-pending handoff rows are included regardless of the edition's current status, because such a row still counts as an active download and therefore suppresses automatic search for that book indefinitely. An absent source becomes terminal `SourceMissing` history and its edition returns to `Missing`; `Downloading` and `Queued` editions with no files and nothing in flight fall back to `Missing` for the same reason. Registered files take precedence and set the edition to `Imported`, except while the client still exposes an in-flight import for that edition, which keeps its live status so reconciliation stays idempotent against `DownloadService`. Cached, stale, unavailable, and unknown client snapshots never clear a blocked state. Reconciliation with `dryRun=false` commits the reviewed plan, and a later snapshot containing the exact source restores `ImportBlocked`.

Wanted and Activity columns drag to resize through the shared `useColumnResize` composable, which writes pixel widths to `grid-template-columns` on both the header and the rows. The title column is emitted as `minmax(width, 1fr)` rather than a fixed pixel width so the grid always fills its container — all-fixed columns left dead space on a wide window and pushed the whole page sideways on a narrow one — and its dragged width acts as the floor while slack is absorbed there. Both grid containers set `overflow-x: auto` so a table wider than the viewport scrolls within itself instead of moving the page. Unlike sort state these persist per browser in local storage, because a column widened to read long titles is a layout preference rather than a momentary question; double-clicking a handle restores the defaults. Stored widths below a column's configured minimum are rejected on load, so a value left over from an older column set cannot collapse a column to an ungrabbable sliver, and dragging is ignored entirely below the 768px breakpoint where both tables become card layouts. Fixed columns (the Wanted poster and both action columns) never resize.

Activity's Clear button removes every finished entry — completed, imported, failed, import-blocked, and source-missing — and leaves anything still queued or downloading running. Removals are issued one at a time rather than in parallel because they reach the download clients, and a burst of concurrent deletes is what makes a client start dropping requests. A partial failure reports how many were removed and leaves the rest listed rather than claiming success.

Wanted and Activity columns sort on click through the shared `useTableSort` composable: first click ascending, next descending. State lives in the composable rather than in storage, so it resets when the view is left and re-entered — a sort answers a question in the moment rather than being a preference to carry forward. Accessors read what the row displays (first author, resolved quality profile name, formatted status) so the order matches what is on screen, except for progress and ETA, which sort on their raw numbers so 9% precedes 10%. Rows with no value stay at the bottom in both directions, and ties keep their original relative order. The Downloads tab is a card list rather than a column table and has no sortable headers.

Text colours use `var(--text-primary)` rather than a hardcoded white. 230 rules across 61 components painted text `#fff` regardless of theme, which is invisible against light mode's `#f4f7fa` background. White is kept only where the rule paints its own solid, non-themed surface — buttons on a brand colour, badges, and the artwork scrim in `status-overlay` — since those stay dark in both themes.

Appearance preferences are frontend-only and stored per browser in local storage. The canonical polished mark is `fe/public/bookmarkarr-logo.png`; the Bookmarkarr, Ocean, Forest, and Plum palettes and System/Light/Dark modes are implemented in `fe/src/services/appearance.ts` and `fe/src/styles/appearance.css`.

## Tech Stack

- Language/Framework: C# 14, .NET 10, ASP.NET Core, Vue 3, TypeScript 6, Vite 8
- Database: SQLite with Entity Framework Core migrations
- Key Dependencies: EF Core, Serilog, SignalR, Pinia, Axios-compatible fetch API, xUnit, Vitest
- Frontend security override: PostCSS 8.5.25 or newer is enforced in `fe/package.json`; the lockfile is refreshed with patched compatible transitive dependencies and must pass `npm audit`

## Prerequisites

- Docker Engine and Docker Compose v2
- For source recreation: Git, .NET 10 SDK, Node.js 24, npm, Python 3.11+
- `portctl` when assigning a host port on the managed host
- Optional externally deployed Prowlarr, NZBGet/SABnzbd, qBittorrent, or RDT Client

## Environment Variables

| Variable | Description | Example |
|----------|-------------|---------|
| BOOKMARKARR_IMAGE | Private/source-tree image override; public Compose pins GHCR | ghcr.io/gitsumhubs/bookmarkarr:latest |
| BOOKMARKARR_PORT | Host web port | 3018 |
| BOOKMARKARR_PUBLIC_URL | Public browser URL | https://books.example.test |
| BOOKMARKARR_AUTHENTICATION_REQUIRED | Require interactive authentication | true |
| BOOKMARKARR_API_KEY | Optional fixed API key | empty |
| BOOKMARKARR_CONFIG_PATH | Host config mount | ./data/config |
| BOOKMARKARR_AUDIOBOOKS_PATH | Host audiobook mount | ./data/audiobooks |
| BOOKMARKARR_EBOOKS_PATH | Host ebook mount | ./data/ebooks |
| BOOKMARKARR_DOWNLOADS_PATH | Host download mount | ./data/downloads |
| BOOKMARKARR_AUDIOBOOK_CATEGORY | Download category | audiobooks |
| BOOKMARKARR_EBOOK_CATEGORY | Download category | ebooks |
| PROWLARR_DOCKER_NETWORK | Optional existing Prowlarr network | replace_with_prowlarr_network |
| NZBGET_DOCKER_NETWORK | Optional existing NZBGet network | replace_with_nzbget_network |
| RDTCLIENT_DOCKER_NETWORK | Optional existing RDT Client network | replace_with_rdtclient_network |
| COMPOSE_FILE | Optional merged Compose files | docker-compose.yml:docker-compose.external-networks.yml |
| PUID | Runtime UID | 1000 |
| PGID | Runtime GID | 1000 |
| UMASK | Created-file mask | 022 |
| TZ | IANA timezone | UTC |
| BOOKMARKARR_LOG_LEVEL | Minimum application log level | Information |

The authoritative full list, including logging variables, is [.env.example](.env.example).

## Port Configuration

- Default host port: 3018; container port: 4545
- Registered in portctl as: bookmarkarr

Before recreating on another managed host, choose and register a free web port:

```bash
portctl find web
portctl check 3018
portctl allocate 3018 bookmarkarr
portctl list
portctl status
```

## Setup Instructions

### Image-Only Deployment

```bash
mkdir -p bookmarkarr/data/config bookmarkarr/data/audiobooks bookmarkarr/data/ebooks bookmarkarr/data/downloads
cd bookmarkarr
curl -fsSLO https://raw.githubusercontent.com/gitsumhubs/Bookmarkarr/main/docker-compose.yml
docker compose pull
docker compose up -d
docker compose ps
curl --fail http://localhost:3018/api/v1/system/ready
```

The downloaded file is runnable without `.env`. Edit its host volume paths, identity, timezone, or port directly; alternatively create `.env` using the optional variable names in `.env.example`.

### Source Recreation

```bash
git clone https://github.com/gitsumhubs/Bookmarkarr.git bookmarkarr-source
cd bookmarkarr-source
docker compose -f docker-compose.yml -f docker-compose.dev.yml build
docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d
```

## Directory Structure

```text
bookmarkarr.api/             ASP.NET controllers, middleware, startup, static UI
bookmarkarr.application/     workflows and integration contracts
bookmarkarr.domain/          books, editions, downloads, history, core rules
bookmarkarr.infrastructure/  EF Core, indexer/client adapters, importing, notifications
fe/                          Vue application and frontend tests
tests/                       xUnit backend/API/database/regression tests and mocks
tools/bookmarkarr-migrate/   dry-run-first source migration utility
docs/                        operations and design documentation
.github/workflows/           CI and version-tag GHCR publication
docker/                      runtime image preparation scripts
docker-compose.external-networks.example.yml  sanitized optional network override template
data/                        ignored local runtime mounts
**/__pycache__/              ignored generated Python bytecode (never tracked)
```

## Configuration Files

- `docker-compose.yml`: standalone one-service deployment using the public GHCR image; no build section or source checkout required; includes Linux host-gateway resolution for `host.docker.internal`
- `docker-compose.dev.yml`: optional source-build override
- `docker-compose.external-networks.example.yml`: sanitized template for attaching Bookmarkarr to externally managed service networks
- A deployment may add its own ignored Compose override for host-specific external networks, mounts, or routing
- `.env.example`: portable deployment configuration
- `Dockerfile`: .NET/Vue multi-stage image
- `Directory.Build.props` and `Directory.Packages.props`: shared .NET build and locked dependency versions
- `bookmarkarr.infrastructure/Persistence/Migrations`: database schema evolution
- `docs/BUGFIXES.md`: append-only inherited and production regression ledger with validation evidence
- `.github/workflows/ci.yml`: test/build checks
- `.github/workflows/publish-ghcr.yml`: semantic tag multi-architecture publication

The public repository is `gitsumhubs/Bookmarkarr`, and its package is public for anonymous Compose pulls. Semantic version tags publish `linux/amd64` and `linux/arm64` images plus `latest`.

Public examples and comments must not use credential-shaped placeholder values; GitHub push protection should pass without bypasses.

## Running the Project

Development without Compose:

```bash
dotnet run --project bookmarkarr.api/Bookmarkarr.Api.csproj
npm run --prefix fe dev
```

Production-style Compose:

```bash
docker compose up -d
docker compose logs -f bookmarkarr
docker compose down
```

For Docker-name connectivity to separately managed Prowlarr, NZBGet, or RDT Client, add the relevant pre-existing external networks directly to the deployment's `docker-compose.yml`. The checked-in `docker-compose.external-networks.example.yml` remains an optional source-checkout template for operators who prefer multiple Compose files.

For host-published services, the root Compose file maps `host.docker.internal` to `host-gateway`; URLs such as `host.docker.internal:6789` work without joining another stack's bridge network. Configure two Bookmarkarr client entries when one endpoint handles both media types because each entry persists one category: `audiobooks` with `/downloads/audiobooks` and `ebooks` with `/downloads/ebooks`.

## Database and Migration

Runtime data is in `${BOOKMARKARR_CONFIG_PATH}/database/bookmarkarr.db`. EF migrations run at startup. `MediaDefaultsSeeder` also performs additive repairs for missing edition defaults, legacy book quality profiles, and missing audiobook `BasePath` values; it never replaces an existing path. Seeded default quality profiles carry `MustNotContain = ["hindi", "arabic", "spanish"]` so a fresh deployment rejects foreign-language editions until an operator removes a word; existing profiles are never modified on upgrade. The words match as case-insensitive substrings of the release title. Never edit a live SQLite file; back up the config directory first. The Listenarr migration is never automatic. Follow [docs/MIGRATION.md](docs/MIGRATION.md), complete a successful dry run, review its plan, and explicitly supply the generated confirmation token for commit.

## Verification

```bash
dotnet test bookmarkarr.slnx -c Release -p:SkipFrontendBuild=true
npm ci --prefix fe
npm run --prefix fe type-check
npm run --prefix fe test:unit
python3 -m unittest discover -s tools/bookmarkarr-migrate -p 'test_*.py'
docker compose -f docker-compose.yml -f docker-compose.dev.yml config --quiet
docker build -t bookmarkarr:local .
curl --fail http://localhost:3018/api/v1/system/ready
```

Filesystem-mutating tests create unique paths below the operating system temporary directory. They must never require write access to a host-root media or mount path in local or CI runs.

The download-client/root/add/queue regression group covers top-level category normalization, PUT upsert, download-path responses, rejection without a root, cleanup of persisted application-directory output, non-cyclic add DTOs, selected-root containment, opt-in Goodreads audiobook search handoff, Goodreads `BasePath` creation, startup path repair, resilient import derivation, Wanted active-download state, fresh-snapshot-only library status reconciliation, and independent scopes for concurrent client polls.

## Release Publishing

Pushing a `vX.Y.Z` tag builds linux/amd64 and linux/arm64 and pushes to GHCR.

The build cache is `type=gha,mode=min`, deliberately not `mode=max`. Caching every intermediate
layer of a multi-arch .NET build grows past GitHub's 10 GB per-repository cache limit within a few
releases. Once over the limit the cache service starts failing, and buildx surfaces that as
`denied: permission_denied ... 403` on the *image push* — so the symptom looks like a registry
permission problem when the registry is fine. If a release fails that way, check
`GET /repos/{owner}/{repo}/actions/cache/usage` before touching package permissions, and purge
with `DELETE /repos/{owner}/{repo}/actions/caches/{id}`.

## Troubleshooting

### Readiness reports pending migrations

Inspect container logs and verify the config mount is writable by `PUID:PGID`. Restore a consistent config backup before retrying after a database failure.

### Add is blocked because no root folder exists

Add `/audiobooks` and `/ebooks` under Settings → Root Folders and select the correct root in the add dialog. Bookmarkarr intentionally leaves `OutputPath` empty and rejects the add instead of writing into `/app/`. Existing persisted values equal to the application directory are cleared during settings initialization.

### External service is unreachable

Use a URL routable from inside the Bookmarkarr container. `localhost` points to Bookmarkarr itself. Test the connection in Settings and verify the external service allows its API path and credentials.

### Files do not import

Ensure the download client and Bookmarkarr see the same download data through the configured mount or a remote path mapping. Check that audio files use recognized audio extensions and ebooks use recognized ebook extensions.

For qBittorrent/RDT single-file torrents reported as `Book.m4b/Book.m4b` or `Book.epub/Book.epub`, map the outer download directory normally. Bookmarkarr resolves only the exact same-named nested media file, rejects reparse-point escape, and does not select unrelated sibling files. Exhausted imports can be safely queued again from the Activity UI or with authenticated `POST /api/v1/downloads/{id}/retry-import` after correcting the path or mount.

Older database rows may have a populated audiobook edition root but an empty legacy `Books.BasePath`. On startup, Bookmarkarr derives the missing per-book path using the current folder naming pattern. Existing custom paths are never repointed. Completed-download import uses the same derivation as a fallback, so after upgrading, retry an `Import Blocked` job instead of downloading the release again.

If the source for a blocked import is no longer present in its download client, first run authenticated `POST /api/v1/library/status/reconcile?dryRun=true`. Only commit the reviewed result with `dryRun=false` when every relevant client reports a healthy live snapshot. The commit preserves the download as `SourceMissing` history and returns the edition to `Missing`; it does not delete or move media. If the response reports skipped unavailable downloads, restore client connectivity and rerun rather than changing the database manually.

### Port conflict

```bash
portctl check 3018
portctl list
portctl status
```

Choose another free web port, update `.env`, and register it before starting the service.

### Duplicate application-settings errors on first boot

Current Bookmarkarr serializes the singleton settings row and the complete first-boot initialization sequence across hosted-service scopes. A clean startup should create exactly one `ApplicationSettings` row without unique-key or optimistic-concurrency errors. If those errors appear after recreating an older deployment, confirm the running image matches the intended release, preserve the config directory for diagnosis, and review `docs/BUGFIXES.md` entry BF-009 before retrying.
