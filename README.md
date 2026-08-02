# Bookmarkarr

Bookmarkarr is a self-hosted manager for unified books with independently managed audiobook and ebook editions. It searches external indexers, submits to external download clients, imports only media appropriate to the requested edition, and provides edition-aware library, wanted, calendar, activity, history, collection, bulk-action, and notification data. It is a full-source fork of Listenarr 1.2.2 with attribution retained in [NOTICE](NOTICE).

![Bookmarkarr mark](fe/public/bookmarkarr-logo.png)

## Tech Stack

- .NET 10 / ASP.NET Core / Entity Framework Core / SQLite
- Vue 3, TypeScript, Vite, Pinia
- Docker with a single application service
- External Prowlarr/Newznab/Torznab, NZBGet, SABnzbd, qBittorrent, and RDT Client

## Prerequisites

- Docker Engine with Docker Compose v2 for normal deployment
- A separately managed indexer and download client; Bookmarkarr does not bundle them
- Writable config, audiobook, ebook, and download directories

For source development, use the .NET 10 SDK, Node.js 24, npm, Python 3.11+, and Docker.

## Docker Compose Deployment

A normal installation needs only Docker Compose and one `docker-compose.yml`. It pulls the public multi-architecture image from GHCR; cloning this repository, building source, and creating an `.env` file are not required.

Create a deployment directory and download the ready-to-run Compose file:

```bash
mkdir -p bookmarkarr/data/config bookmarkarr/data/audiobooks bookmarkarr/data/ebooks bookmarkarr/data/downloads
cd bookmarkarr
curl -fsSLO https://raw.githubusercontent.com/gitsumhubs/Bookmarkarr/main/docker-compose.yml
docker compose pull
docker compose up -d
docker compose ps
```

The essential service definition is a conventional image-only Compose service:

```yaml
---
services:
  bookmarkarr:
    image: ghcr.io/gitsumhubs/bookmarkarr:latest
    container_name: bookmarkarr
    environment:
      - PUID=1000
      - PGID=1000
      - UMASK=022
      - TZ=Etc/UTC
    volumes:
      - ./data/config:/app/config
      - ./data/audiobooks:/audiobooks
      - ./data/ebooks:/ebooks
      - ./data/downloads:/downloads
    ports:
      - 3018:4545
    restart: unless-stopped
```

Open `http://localhost:3018` after the health status becomes `healthy`. Edit the host side of the volume mappings if your libraries or download staging directory live elsewhere. The selected `PUID:PGID` must be able to write to all four host paths; never reuse another application's config/database directory.

The checked-in Compose file has the same image-only service plus a health check, bounded Docker log rotation, and optional `${VARIABLE:-default}` overrides. It works unchanged without `.env`; copy [.env.example](.env.example) to `.env` only when you want to override those defaults without editing YAML.

### Connect external services

Bookmarkarr does not bundle Prowlarr, indexers, or download clients. Their URLs must be reachable from inside the Bookmarkarr container.

- If those services expose LAN or reverse-proxy URLs that containers can reach, use those URLs and leave the Compose file unchanged.
- If they are addressed by Docker service name, add their existing Docker network to the same `docker-compose.yml`:

```yaml
services:
  bookmarkarr:
    networks:
      - default
      - media-services

networks:
  media-services:
    external: true
    name: existing-download-stack-network
```

Find the real network name with:

```bash
docker network ls
```

Every declared external network must already exist. Multiple services can share one existing network, or you can add one entry per external Compose project. See [Docker deployment](docs/DOCKER.md) for more examples.

## First-Run Configuration

1. Open Bookmarkarr and go to **Settings → Root Folders**. Add `/audiobooks` as the default audiobook library root. Ebook imports use the separately mounted `/ebooks` root.
2. Open **Settings → Download Clients** and add each client using a URL or Docker hostname reachable from Bookmarkarr. Use the built-in Test action before saving.
3. Create both `audiobooks` and `ebooks` categories in each applicable client. Save `audiobooks` as the client's normal Bookmarkarr category; ebook submissions are assigned to `ebooks`.
4. For RDT Client, select the **qBittorrent** client type and enter RDT's qBittorrent-compatible API host, port, and credentials.
5. Make the download client and Bookmarkarr see completed data at the same `/downloads` container path. If the client reports a different path, add a mapping under **Settings → Download Clients → Add Mapping** from the client-reported path to `/downloads`.
6. Open **Settings → Indexers → Import from Prowlarr**. Enter a reachable Prowlarr URL such as `http://prowlarr:9696`, its API key from Prowlarr **Settings → General**, and an optional tag filter. Import the indexers and test each one.
7. Configure AudiobookBay in a Prowlarr installation or maintained indexer definition that supports it, then verify it inside Prowlarr. Bookmarkarr accepts the resulting Torznab magnet directly; Bookmarkarr does not install the indexer definition and the former custom Torznab proxy is not required.
8. Review **Settings → Quality Profiles** and **Settings → General Settings**, then add or import a book and choose the intended audiobook or ebook edition.

For field-level details and path examples, see [External clients](docs/EXTERNAL_CLIENTS.md).

## Verify the First Download

Use a legal test item and confirm this sequence:

1. Every indexer and download-client connection test succeeds.
2. Audiobook search mode returns only audio or legitimate audiobook bundles; ebook mode returns only recognized ebook releases.
3. The selected release appears in **Activity** and in the expected `audiobooks` or `ebooks` client category.
4. After completion, the primary file is imported under `/audiobooks` or `/ebooks` and appears in the library.
5. If import does not start, compare the path reported by the client with Bookmarkarr's `/downloads` path and add a remote path mapping if they differ.

Useful checks:

```bash
docker compose ps
docker compose logs --tail=200 bookmarkarr
curl --fail http://localhost:3018/api/v1/system/ready
```

Common causes are a Docker network that Bookmarkarr has not joined, an unreachable `localhost` client URL, mismatched download paths, or host directories that are not writable by `PUID:PGID`. RDT/qBittorrent single-file layouts such as `Book.m4b/Book.m4b` and `Book.epub/Book.epub` are handled automatically when the outer path is mapped correctly.

## Features

- One bibliographic book with separate audiobook and ebook editions
- Independent monitoring, quality profile, root, download category, search, import, upgrade, and wanted state per edition
- Server-derived strict release filtering: audio and legitimate audio bundles for audiobooks; recognized ebook releases only for ebooks
- Separate file registries; mixed audio bundles route recognized ebook sidecars to the ebook edition when present
- Goodreads CSV upload, preview, match resolution, format selection, and idempotent additive commit without automatic searches
- External Prowlarr including AudiobookBay, direct Newznab/Torznab, NZBGet, SABnzbd, qBittorrent, and RDT Client
- Stable download job IDs, `pausedUP` completion support, resilient sidecar handling and bounded/manual retries, and AudiobookBay magnet support
- Safe recovery of qBittorrent/RDT single-file layouts whose outer directory repeats the media filename (for example `Book.m4b/Book.m4b`)
- Read-only-source, dry-run-first, transactional Listenarr migration utility
- Responsive web UI, API documentation, history, notifications, backups, and multi-architecture release automation
- Browser-persisted appearance settings with a polished Bookmarkarr theme, simple Ocean, Forest, and Plum palettes, and System/Light/Dark color modes

## Future Development

- Add a Radarr-style bulk import workflow for existing ebook libraries: scan the configured ebook root, match files to book/ebook editions, review ambiguous matches, and import selected files without requiring a new download.

## Configuration

All deployment variables and portable mount defaults are documented in [.env.example](.env.example). Configure clients in the UI with container-reachable URLs and distinct `audiobooks` and `ebooks` categories. Do not point container URLs at `localhost` unless the service is in the same container.

Documentation:

- [Docker deployment](docs/DOCKER.md)
- [Architecture](docs/ARCHITECTURE.md) and [API](docs/API.md)
- [Goodreads import](docs/GOODREADS.md)
- [Listenarr migration](docs/MIGRATION.md)
- [External clients](docs/EXTERNAL_CLIENTS.md)
- [Inherited and production bug-fix ledger](docs/BUGFIXES.md)
- [Backups](docs/BACKUPS.md)
- [Releases and GHCR](docs/RELEASES.md)
- [Recreating this project](RECREATE.md)

## Development and Testing

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

Adapter tests use mocks and fixtures; the test suite does not download copyrighted production content.
The locked frontend dependency graph enforces PostCSS 8.5.25 or newer; keep the override when refreshing the lockfile so the source-map path traversal advisory does not reappear.

## License

Bookmarkarr is distributed under the GNU Affero General Public License v3 or later. See [LICENSE](LICENSE) and [NOTICE](NOTICE). Network operators must make the corresponding source available as required by the AGPL.
