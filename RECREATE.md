# Bookmarkarr

## Overview

Bookmarkarr is a standalone full-source Listenarr 1.2.2 fork for managing audiobook and ebook editions independently under a unified book. It uses external indexers and download clients and ships as one application container. The current bulk library-import workflow is audiobook-focused; a Radarr-style scan, match-review, and import workflow for existing ebook files remains planned future work.

Normal operators deploy from the public `ghcr.io/gitsumhubs/bookmarkarr:latest` image with the standalone root `docker-compose.yml`. The file contains no build configuration and requires neither a source checkout nor `.env`; environment-variable overrides and external Docker networks remain optional.

Library adds require either a configured root folder or an explicit destination. Empty and legacy application-directory output paths are never used for media storage. Download-client API compatibility maps a top-level category into `SettingsJson`, exposes category/download path on reads, accepts both POST upsert and `PUT /download-clients/{id}`, and preserves stored credentials when redacted placeholders are submitted. Concurrent client queue polls resolve an isolated scoped gateway per client so EF Core contexts are not shared across parallel work.

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

- `docker-compose.yml`: standalone one-service deployment fixed to the public GHCR image; no build section or source checkout required; includes Linux host-gateway resolution for `host.docker.internal`
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

The download-client/root/add/queue regression group covers top-level category normalization, PUT upsert, download-path responses, rejection without a root, cleanup of persisted application-directory output, non-cyclic add DTOs, selected-root containment, and independent scopes for concurrent client polls.

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

### Port conflict

```bash
portctl check 3018
portctl list
portctl status
```

Choose another free web port, update `.env`, and register it before starting the service.

### Duplicate application-settings errors on first boot

Current Bookmarkarr serializes the singleton settings row and the complete first-boot initialization sequence across hosted-service scopes. A clean startup should create exactly one `ApplicationSettings` row without unique-key or optimistic-concurrency errors. If those errors appear after recreating an older deployment, confirm the running image matches the intended release, preserve the config directory for diagnosis, and review `docs/BUGFIXES.md` entry BF-009 before retrying.
