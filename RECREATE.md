# Bookmarkarr

## Overview

Bookmarkarr is a standalone full-source Listenarr 1.2.2 fork for managing audiobook and ebook editions independently under a unified book. It uses external indexers and download clients and ships as one application container. The current bulk library-import workflow is audiobook-focused; a Radarr-style scan, match-review, and import workflow for existing ebook files remains planned future work.

Appearance preferences are frontend-only and stored per browser in local storage. The canonical polished mark is `fe/public/bookmarkarr-logo.png`; the Bookmarkarr, Ocean, Forest, and Plum palettes and System/Light/Dark modes are implemented in `fe/src/services/appearance.ts` and `fe/src/styles/appearance.css`.

## Tech Stack

- Language/Framework: C# 14, .NET 10, ASP.NET Core, Vue 3, TypeScript 6, Vite 8
- Database: SQLite with Entity Framework Core migrations
- Key Dependencies: EF Core, Serilog, SignalR, Pinia, Axios-compatible fetch API, xUnit, Vitest
- Frontend security override: PostCSS 8.5.25 or newer is enforced in `fe/package.json`; the lockfile is refreshed with patched compatible transitive dependencies and must pass `npm audit`

## Prerequisites

- Git
- Docker Engine and Docker Compose v2
- For source builds: .NET 10 SDK, Node.js 24, npm, Python 3.11+
- `portctl` when assigning a host port on the managed host
- Optional externally deployed Prowlarr, NZBGet/SABnzbd, qBittorrent, or RDT Client

## Environment Variables

| Variable | Description | Example |
|----------|-------------|---------|
| BOOKMARKARR_IMAGE | Normal deployment image | ghcr.io/owner/bookmarkarr:latest |
| BOOKMARKARR_CONTAINER_NAME | Compose container name | bookmarkarr |
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
| BOOKMARKARR_PROWLARR_URL | External Prowlarr URL | http://prowlarr:9696 |
| BOOKMARKARR_NZBGET_URL | External NZBGet URL | http://nzbget:6789 |
| BOOKMARKARR_SABNZBD_URL | External SABnzbd URL | http://sabnzbd:8080 |
| BOOKMARKARR_QBITTORRENT_URL | External qBittorrent URL | http://qbittorrent:8080 |
| BOOKMARKARR_RDTCLIENT_URL | External RDT Client URL | http://rdtclient:6500 |
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

```bash
git clone <bookmarkarr-repository-url> bookmarkarr
cd bookmarkarr
cp .env.example .env
mkdir -p data/config data/audiobooks data/ebooks data/downloads
docker compose pull
docker compose up -d
docker compose ps
curl --fail http://localhost:3018/api/v1/system/ready
```

For a local developer image:

```bash
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

- `docker-compose.yml`: portable one-service pull-based deployment
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

The public repository is `gitsumhubs/Bookmarkarr`. Enable GitHub Actions, push a semantic version tag, and make the resulting GHCR package public if anonymous Compose pulls are desired.

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

For Docker-name connectivity to separately managed Prowlarr, NZBGet, or RDT Client, copy `docker-compose.external-networks.example.yml` without the `.example` suffix, set the existing network names in `.env`, remove unused networks, and set `COMPOSE_FILE=docker-compose.yml:docker-compose.external-networks.yml` before validating with `docker compose config`.

## Database and Migration

Runtime data is in `${BOOKMARKARR_CONFIG_PATH}/database/bookmarkarr.db`. EF migrations run at startup. Never edit a live SQLite file; back up the config directory first. The Listenarr migration is never automatic. Follow [docs/MIGRATION.md](docs/MIGRATION.md), complete a successful dry run, review its plan, and explicitly supply the generated confirmation token for commit.

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

## Troubleshooting

### Readiness reports pending migrations

Inspect container logs and verify the config mount is writable by `PUID:PGID`. Restore a consistent config backup before retrying after a database failure.

### External service is unreachable

Use a URL routable from inside the Bookmarkarr container. `localhost` points to Bookmarkarr itself. Test the connection in Settings and verify the external service allows its API path and credentials.

### Files do not import

Ensure the download client and Bookmarkarr see the same download data through the configured mount or a remote path mapping. Check that audio files use recognized audio extensions and ebooks use recognized ebook extensions.

For qBittorrent/RDT single-file torrents reported as `Book.m4b/Book.m4b` or `Book.epub/Book.epub`, map the outer download directory normally. Bookmarkarr resolves only the exact same-named nested media file, rejects reparse-point escape, and does not select unrelated sibling files. Exhausted imports can be safely queued again from the Activity UI or with authenticated `POST /api/v1/downloads/{id}/retry-import` after correcting the path or mount.

### Port conflict

```bash
portctl check 3018
portctl list
portctl status
```

Choose another free web port, update `.env`, and register it before starting the service.

### Duplicate application-settings errors on first boot

Current Bookmarkarr serializes the singleton settings row and the complete first-boot initialization sequence across hosted-service scopes. A clean startup should create exactly one `ApplicationSettings` row without unique-key or optimistic-concurrency errors. If those errors appear after recreating an older deployment, confirm the running image matches the intended release, preserve the config directory for diagnosis, and review `docs/BUGFIXES.md` entry BF-009 before retrying.
