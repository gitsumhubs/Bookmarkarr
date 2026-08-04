# Bookmarkarr

Bookmarkarr finds, downloads, imports, and organizes your **audiobooks and ebooks** — the
same way Radarr and Sonarr handle movies and TV. Point it at your indexers and download
client, add the books you want, and it handles grabbing and filing them.

Audiobooks and ebooks are equal citizens here. One book can have an audiobook edition, an
ebook edition, or both, and each edition gets its own monitoring, quality profile, library
root, download category, and wanted state. It isn't an audiobook app with ebooks bolted on.

![Bookmarkarr mark](fe/public/bookmarkarr-logo.png)

---

## Quick start

You need Docker and one file. Save this as `docker-compose.yml`:

```yaml
services:
  bookmarkarr:
    image: ghcr.io/gitsumhubs/bookmarkarr:latest
    container_name: bookmarkarr
    environment:
      - PUID=1000          # `id -u` — must be able to write the folders below
      - PGID=1000          # `id -g`
      - UMASK=022
      - TZ=Etc/UTC         # e.g. America/New_York
    volumes:
      # Left side = folders on YOUR machine. Point these wherever your files live.
      - ./data/config:/app/config          # database + settings — BACK THIS UP
      - ./data/audiobooks:/audiobooks
      - ./data/ebooks:/ebooks
      # Your download client must see this same folder, ideally at the same path.
      - ./data/downloads:/downloads
    ports:
      - 3018:4545          # change 3018 if it's taken; leave 4545 alone
    extra_hosts:
      - "host.docker.internal:host-gateway"
    restart: unless-stopped

    # NZBGet/Prowlarr/torrent client in their own compose files? Uncomment BOTH
    # blocks below, using the real names from `docker network ls`. See step 1.
    #
    # networks: [default, nzbget, prowlarr, torrent]

# networks:                       # ← this block too, not just the one above
#   nzbget:   {external: true, name: nzbget_default}
#   prowlarr: {external: true, name: prowlarr_default}
#   torrent:  {external: true, name: rdtclient_default}
```

> **Network names come from the folder** the other compose file lives in, not the service
> name — a Prowlarr in a folder called `prowlarr-abb` creates `prowlarr-abb_default`. Always
> copy them from `docker network ls` rather than guessing.
>
> **Skip this entirely if** everything is in one compose file, or your services run on the
> host — then just use their LAN URL like `http://192.168.1.50:6789`.

Then start it:

```bash
mkdir -p data/config data/audiobooks data/ebooks data/downloads
docker compose up -d
```

Open <http://localhost:3018>.

> `PUID:PGID` must be able to write all four host paths. Don't point the config volume at
> another app's config directory.

### What happens on first run

Bookmarkarr creates these on first start, **only if missing**:

- A **Default Audiobook** quality profile (prefers M4B, then MP3/M4A/FLAC/Opus)
- A **Default Ebook** quality profile (prefers EPUB, then AZW3/MOBI/PDF)
- Library roots for `/audiobooks` and `/ebooks`
- Staging folders `/downloads/audiobooks` and `/downloads/ebooks`

Nothing you've configured is overwritten. If something looks missing, check
`docker logs bookmarkarr` for mount warnings.

---

## Setting it up

### 1. Check Bookmarkarr can reach your other containers

If you uncommented the `networks:` block in Quick Start, confirm it took effect:

```bash
docker inspect bookmarkarr --format '{{range $k,$v := .NetworkSettings.Networks}}{{$k}} {{end}}'
```

You should see the networks you added. If one is missing, the name in your compose doesn't
match `docker network ls`.

Quick sanity check that a hostname resolves:

```bash
docker exec bookmarkarr getent hosts nzbget
```

No output means Bookmarkarr can't see it — revisit the `networks:` block, or just use the
LAN URL (`http://192.168.1.50:6789`) in the next step instead.

### 2. Download clients

**Settings → Download Clients**. NZBGet, SABnzbd, qBittorrent, and RDT Client are supported.
Use a URL reachable *from inside the container*, and hit **Test** before saving.

Create both an `audiobooks` and an `ebooks` category. One Bookmarkarr client entry owns one
category, so if a single downloader handles both formats, add **two entries** for the same
endpoint:

| Entry | Category | Download path |
|---|---|---|
| `NZBGet (audiobooks)` | `audiobooks` | `/downloads/audiobooks` |
| `NZBGet (ebooks)` | `ebooks` | `/downloads/ebooks` |

For RDT Client, choose the **qBittorrent** client type and enter RDT's qBittorrent-compatible
API host, port, and credentials.

Bookmarkarr and your download client should see finished files at the **same** `/downloads`
path. If they don't, add a mapping under **Settings → Download Clients → Add Mapping**.

### 3. Indexers

**Settings → Indexers → Import from Prowlarr**. Enter a reachable Prowlarr URL (usually
`http://prowlarr:9696`), the API key from **Prowlarr → Settings → General**, and an optional
tag filter. Import, then **Test** each one.

Direct Newznab/Torznab indexers work too.

### 4. Add books

Search from **Add New**, or import your Goodreads shelf (below).

---

## Using AudiobookBay

AudiobookBay works through Prowlarr, and magnet results work directly.

**Stock Prowlarr does not include AudiobookBay.** It isn't in the official indexer
definitions, and Bookmarkarr can't install definitions into Prowlarr — so you need a
Prowlarr build that ships it.

One community option is the [prowlarr-abb](https://github.com/BitlessByte0/prowlarr-abb)
fork (`bitlessbyte/prowlarr`), a LinuxServer.io-based image with the definition included.
It's third-party and unofficial: it only picks up upstream Prowlarr releases when its
maintainer rebuilds, so check it's still actively maintained before depending on it. Any
build providing a working AudiobookBay definition will do.

Once Prowlarr has it:

1. **In Prowlarr**, add AudiobookBay and confirm it passes Prowlarr's own **Test**.
2. Check its category mappings distinguish audio/audiobook from book/ebook — Bookmarkarr
   uses those to route results.
3. **In Bookmarkarr**, import via **Settings → Indexers → Import from Prowlarr**, then run
   **Test**.
4. AudiobookBay returns **magnets**, so you need a torrent-capable client — qBittorrent or
   RDT Client — configured with the `audiobooks` category.

Bookmarkarr filters results server-side, so an audiobook search only returns audiobooks and
an ebook search only returns ebooks.

---

## Importing from Goodreads

**Library → Import → Goodreads**:

1. Go to [goodreads.com/review/import](https://www.goodreads.com/review/import) and click
   **Export Library**.
2. Download the CSV it generates.
3. Upload it and **preview** the import.
4. Choose which books to add, resolve any ambiguous matches, and pick the audiobook edition,
   the ebook edition, or both.

Importing is **additive** and starts no searches. Nothing downloads until you press Search,
or tick *search after import*. Your CSV isn't retained. Audiobook imports record their
per-book destination from the selected root and folder naming pattern when they are created.

---

## Verifying your first download

1. Indexer and download-client tests pass.
2. The release appears in **Activity** under the expected category.
3. Once complete, the file lands in `/audiobooks` or `/ebooks` and shows in your library.

```bash
docker compose ps
docker compose logs --tail=200 bookmarkarr
curl --fail http://localhost:3018/api/v1/system/ready
```

---

## Troubleshooting

**A book stays "Missing" after searching.** Check the edition has a quality profile on its
detail page — automatic grabs need one, manual searches don't.

**Ebooks aren't importing.** Confirm your client has a separate `ebooks` category pointing at
`/downloads/ebooks`, and check the allowed ebook extensions under **Settings → File
Management**.

**Downloads finish but never import.** Almost always a path mismatch. Compare the path your
client reports with Bookmarkarr's `/downloads` and add a remote path mapping if they differ.
If Activity reports **Import Blocked** because an older book has no destination, restart the
current Bookmarkarr release once. Startup repair derives the missing per-book path without
overwriting custom paths, and the importer independently derives it from the audiobook
edition if a legacy creation path ever omits it again. Retry the existing import; the files
do not need to be downloaded again.

**Bookmarkarr can't reach Prowlarr or the download client.** The URL has to resolve from
*inside* the container — `localhost` only works if the service is in the same container.
If you're addressing services by name, see
[step 1](#1-let-bookmarkarr-reach-your-other-containers): Bookmarkarr must share a Docker
network with them. Check which networks it joined:

```bash
docker inspect bookmarkarr --format '{{range $k,$v := .NetworkSettings.Networks}}{{$k}} {{end}}'
```

**Something looks unconfigured after upgrading.** Check `docker logs bookmarkarr` for
warnings about mounts that are missing or not writable.

Single-file layouts like `Book.m4b/Book.m4b` and `Book.epub/Book.epub` from
qBittorrent/RDT are handled automatically when the outer path is mapped correctly.

---

## Features

- One bibliographic book with independent audiobook and ebook editions
- Per-edition monitoring, quality profile, root folder, download category, search, import,
  upgrade, and wanted state
- Media-aware defaults: one default profile and one default root **per media type**
- Strict server-side release filtering, so ebook searches never surface audiobooks
- Separate file registries; mixed audio bundles route ebook sidecars to the ebook edition
- Goodreads CSV upload, preview, match resolution, format selection, and additive commit
- Shared audiobook destination planning across Goodreads creation, startup repair, previews,
  bulk edits, and completed-download import
- Prowlarr (including AudiobookBay), direct Newznab/Torznab, NZBGet, SABnzbd, qBittorrent,
  and RDT Client
- Live Wanted status — Queued, Downloading, Import Pending, Import Blocked — matched to
  downloads by stable edition ID
- Bulk import of existing audiobook and ebook libraries already on disk
- Read-only-source, dry-run-first, transactional Listenarr migration utility
- Responsive UI, history, notifications, backups, and multi-arch release automation
- Bookmarkarr, Ocean, Forest, and Plum themes with System/Light/Dark modes and a top-bar
  toggle

## Notifications

**Settings → Notifications → Add Webhook**. Slack, Discord, Telegram, Pushover,
Pushbullet, NTFY, Gotify, and generic webhooks are supported. Pick your triggers, then use
**Test** to confirm delivery.

### Gotify

Works the same way Radarr and Sonarr do it:

1. In Gotify, go to **Apps → Create Application** and copy the application token.
2. In Bookmarkarr, add a webhook of type **Gotify** with the URL
   `https://your-gotify-host/message?token=<appToken>`.
3. Choose your triggers and hit **Test**.

Self-hosted instances are detected by the endpoint shape, so your Gotify hostname can be
anything — it doesn't need "gotify" in the name.

## Configuration

| Variable | What it does | Default |
|---|---|---|
| `PUID` / `PGID` | User/group owning created files | `1000` |
| `UMASK` | Permissions mask for new files | `022` |
| `TZ` | Timezone for schedules and logs | `Etc/UTC` |
| `BOOKMARKARR_PORT` | Host port to publish | `3018` |
| `BOOKMARKARR_LOG_LEVEL` | `Debug`, `Information`, `Warning`, `Error` | `Information` |
| `BOOKMARKARR_PUBLIC_URL` | External URL when behind a reverse proxy | empty |
| `BOOKMARKARR_AUTHENTICATION_REQUIRED` | Require login | `false` |
| `BOOKMARKARR_API_KEY` | API key for external tools | empty |
| `BOOKMARKARR_AUDIOBOOK_CATEGORY` | Download category for audiobooks | `audiobooks` |
| `BOOKMARKARR_EBOOK_CATEGORY` | Download category for ebooks | `ebooks` |

| Volume | What goes there |
|---|---|
| `/app/config` | Database and settings — **back this up** |
| `/audiobooks` | Your audiobook library |
| `/ebooks` | Your ebook library |
| `/downloads` | Download staging, shared with your client |

Full details in [.env.example](.env.example).

## Documentation

- [Docker deployment](docs/DOCKER.md)
- [External clients and indexers](docs/EXTERNAL_CLIENTS.md)
- [Goodreads import](docs/GOODREADS.md)
- [Backups](docs/BACKUPS.md)
- [Listenarr migration](docs/MIGRATION.md)
- [Architecture](docs/ARCHITECTURE.md) and [API](docs/API.md)
- [Bug-fix ledger](docs/BUGFIXES.md)
- [Releases and GHCR](docs/RELEASES.md)
- [Recreating this project](RECREATE.md)

## Importing an existing library

Already have books on disk? **Library → Import**, pick a root folder, and scan. Bookmarkarr
lists everything not already in your library, you match each entry, and it files them.

This works for both media types. Scanning an **audiobook** root finds audio files and groups
multi-part books together; scanning an **ebook** root finds EPUB/MOBI/AZW3/PDF and treats
each file as its own book. Files already imported are skipped, so you can rescan freely.

Choose **Copy** to leave your originals untouched, or **Move** to relocate them into the
library layout.

## Development

.NET 10 SDK, Node.js 24, npm, Python 3.11+, and Docker.

```bash
dotnet restore bookmarkarr.slnx --locked-mode
dotnet test bookmarkarr.slnx -c Release -p:SkipFrontendBuild=true
npm ci --prefix fe
npm run --prefix fe type-check
npm run --prefix fe test:unit
python3 -m unittest discover -s tools/bookmarkarr-migrate -p 'test_*.py'
docker compose -f docker-compose.yml -f docker-compose.dev.yml config --quiet
docker build -t bookmarkarr:local .
```

Adapter tests use mocks and fixtures; the suite never downloads copyrighted content. The
locked frontend dependency graph requires PostCSS 8.5.25 or newer — keep the override when
refreshing the lockfile so the source-map path traversal advisory doesn't reappear.

## Tech stack

.NET 10 / ASP.NET Core / EF Core / SQLite · Vue 3 + TypeScript + Vite + Pinia · Docker

## License

Bookmarkarr is distributed under the **GNU Affero General Public License v3 or later**. See
[LICENSE](LICENSE) and [NOTICE](NOTICE). Network operators must make the corresponding source
available as required by the AGPL.

A full-source fork of Listenarr 1.2.2, with attribution retained in [NOTICE](NOTICE).
