# Bookmarkarr

Bookmarkarr finds, downloads, imports, and organizes your **audiobooks and ebooks** — the same way
Radarr and Sonarr handle movies and TV.

Audiobooks and ebooks are equal citizens. One book can have an audiobook edition, an ebook edition,
or both, and each gets its own monitoring, quality profile, library root, download category, and
wanted state.

![Bookmarkarr mark](fe/public/bookmarkarr-logo.png)

---

## Quick start

Save this as `docker-compose.yml`:

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
      # For AudiobookBay: point the left side at Prowlarr's config folder.
      # Otherwise leave it — it's just an empty folder. See "AudiobookBay" below.
      - ./data/prowlarr-config:/prowlarr-config
    ports:
      - 3018:4545          # change 3018 if it's taken; leave 4545 alone
    extra_hosts:
      - "host.docker.internal:host-gateway"
    restart: unless-stopped

    # Prowlarr/NZBGet/torrent client in their own compose files? Uncomment BOTH
    # blocks below, using the real names from `docker network ls`.
    #
    # networks: [default, nzbget, prowlarr, torrent]

# networks:                       # ← this block too, not just the one above
#   nzbget:   {external: true, name: nzbget_default}
#   prowlarr: {external: true, name: prowlarr_default}
#   torrent:  {external: true, name: rdtclient_default}
```

```bash
mkdir -p data/config data/audiobooks data/ebooks data/downloads data/prowlarr-config
docker compose up -d
```

Open <http://localhost:3018>.

> Network names come from the **folder** the other compose file lives in, not the service name — a
> Prowlarr in `prowlarr-abb/` creates `prowlarr-abb_default`. Copy them from `docker network ls`.
> Skip that block entirely if everything is in one compose file or your services run on the host;
> just use their LAN URL, like `http://192.168.1.50:6789`.

On first run Bookmarkarr creates default quality profiles, library roots, and staging folders — only
if they're missing. Both default profiles block `hindi`, `arabic`, and `spanish` release titles so a
new install doesn't grab foreign-language editions; remove a word in **Settings → Quality Profiles**
to allow it.

---

## Setup

**1. Download clients** — **Settings → Download Clients**. NZBGet, SABnzbd, qBittorrent, and RDT
Client. Use a URL reachable *from inside the container* and hit **Test**.

One client entry owns one category, so a single downloader handling both formats needs two entries:

| Entry | Category | Download path |
|---|---|---|
| `NZBGet (audiobooks)` | `audiobooks` | `/downloads/audiobooks` |
| `NZBGet (ebooks)` | `ebooks` | `/downloads/ebooks` |

For RDT Client, choose the **qBittorrent** type and enter RDT's qBittorrent-compatible API details.
Bookmarkarr and your client should see finished files at the **same** `/downloads` path; if not, add
a mapping under **Settings → Download Clients → Add Mapping**.

**2. Indexers** — **Settings → Indexers → Import from Prowlarr**. Enter the Prowlarr URL (usually
`http://prowlarr:9696`) and the API key from **Prowlarr → Settings → General**, then **Test** each
imported indexer. **Test & save connection** stores the connection without importing anything.
Direct Newznab/Torznab indexers work too.

**3. AudiobookBay** (optional) — see below.

**4. Add books** — search from **Add New**, or import a Goodreads shelf.

---

## AudiobookBay

Stock Prowlarr doesn't ship the AudiobookBay definition, and the site 404s anything identifying as
Prowlarr — so you need a fork that includes it and substitutes a user agent, such as
[prowlarr-abb](https://github.com/BitlessByte0/prowlarr-abb). Add AudiobookBay there, test it, then
import it into Bookmarkarr. It returns magnets, so you need a torrent-capable client.

Prowlarr's compiled indexer also reads only the first page of results, capping every search at nine.
**Settings → ABB Patch** replaces it with a paginated definition — three pages by default. To let
Bookmarkarr install that itself, point the `/prowlarr-config` volume at Prowlarr's config folder (the
one holding `config.xml`) and `docker compose up -d`. Without the mount the tab hands you the
definition to copy in by hand and finishes the wiring itself.

Full detail — the mount, manual install, apostrophe handling, the 404 — in
**[docs/AUDIOBOOKBAY.md](docs/AUDIOBOOKBAY.md)**.

---

## Importing books you already have

**Library → Import**, pick a root folder, scan. Bookmarkarr lists what isn't already in your
library, you match each entry, and it files them. Audiobook roots group multi-part books; ebook
roots treat each EPUB/MOBI/AZW3/PDF as its own book. Already-imported files are skipped, so rescan
freely. **Copy** leaves your originals untouched; **Move** relocates them.

**From Goodreads:** export your library at
[goodreads.com/review/import](https://www.goodreads.com/review/import), then **Library → Import →
Goodreads** to upload and preview it. Importing is additive and starts no searches. Your CSV isn't
retained.

**From Listenarr:** see [docs/MIGRATION.md](docs/MIGRATION.md).

---

## Troubleshooting

Longer explanations for each of these: **[docs/TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md)**.

| Symptom | Fix |
|---|---|
| Book stays **Missing** after searching | Give the edition a quality profile — automatic grabs need one |
| Ebooks aren't importing | Separate `ebooks` category → `/downloads/ebooks`; check allowed extensions |
| Downloads finish but never import | Path mismatch — add a remote path mapping |
| **Import Blocked**, source is gone | Reconcile: `POST /api/v1/library/status/reconcile?dryRun=true` |
| Stuck **Downloading**, never re-searched | Same reconcile — a vanished handoff still counts as active |
| Same release grabbed and failing repeatedly | Blocklisted after two failures; review `GET /api/v1/blocklist` |
| Can't reach Prowlarr or the download client | URL must resolve *inside* the container; check shared networks |
| AudiobookBay finds nothing / stops at nine | See [AudiobookBay](docs/AUDIOBOOKBAY.md) |
| Something looks unconfigured after upgrade | `docker logs bookmarkarr` — look for mount warnings |

Health check: `curl --fail http://localhost:3018/api/v1/system/ready`

---

## Features

- One book, independent audiobook and ebook editions — each with its own monitoring, profile, root,
  category, search, import, upgrade, and wanted state
- Strict server-side filtering, so ebook searches never surface audiobooks
- Prowlarr (including AudiobookBay), direct Newznab/Torznab, NZBGet, SABnzbd, qBittorrent, RDT Client
- Live Wanted status — Queued, Downloading, Import Pending, Import Blocked
- A search ladder from precise to broad, so a book held under an awkward title is still found
- **Grab as collection** — a box set lands as many books rather than merging into one
- Per-book release blocklisting after repeated failures
- Optional preservation of original file names, library-wide or per book
- Bulk import of existing libraries, Goodreads CSV import, Listenarr migration
- History, notifications, backups, four themes, System/Light/Dark

## Notifications

**Settings → Notifications → Add Webhook**. Slack, Discord, Telegram, Pushover, Pushbullet, NTFY,
Gotify, and generic webhooks. Pick triggers, then **Test**.

For Gotify, create an application in Gotify and use
`https://your-gotify-host/message?token=<appToken>`. Self-hosted instances are detected by endpoint
shape, so the hostname can be anything.

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
| `/prowlarr-config` | Prowlarr's config, for the AudiobookBay patch — optional |

Full list in [.env.example](.env.example).

## Documentation

- [AudiobookBay](docs/AUDIOBOOKBAY.md) · [Troubleshooting](docs/TROUBLESHOOTING.md)
- [Docker deployment](docs/DOCKER.md) · [External clients and indexers](docs/EXTERNAL_CLIENTS.md)
- [Goodreads import](docs/GOODREADS.md) · [Backups](docs/BACKUPS.md) ·
  [Listenarr migration](docs/MIGRATION.md)
- [Architecture](docs/ARCHITECTURE.md) · [API](docs/API.md) · [Bug-fix ledger](docs/BUGFIXES.md)
- [Releases and GHCR](docs/RELEASES.md) · [Recreating this project](RECREATE.md)

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

Adapter tests use mocks and fixtures; the suite never downloads copyrighted content. The locked
frontend dependency graph requires PostCSS 8.5.25 or newer — keep the override when refreshing the
lockfile so the source-map path traversal advisory doesn't reappear.

## Tech stack

.NET 10 / ASP.NET Core / EF Core / SQLite · Vue 3 + TypeScript + Vite + Pinia · Docker

## License

Bookmarkarr is distributed under the **GNU Affero General Public License v3 or later**. See
[LICENSE](LICENSE) and [NOTICE](NOTICE). Network operators must make the corresponding source
available as required by the AGPL.

A full-source fork of Listenarr 1.2.2, with attribution retained in [NOTICE](NOTICE).
