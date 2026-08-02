# Docker Deployment

## Normal Deployment

Normal Compose pulls a configurable GHCR image and runs one service. No integration containers are bundled.

```bash
cp .env.example .env
mkdir -p data/config data/audiobooks data/ebooks data/downloads
docker compose pull
docker compose up -d
```

The four mounts isolate mutable configuration/database, audiobook media, ebook media, and download staging. Relative defaults make the template portable; production operators may replace them in the untracked `.env`.

## External Docker Networks

Services in separate Compose projects normally occupy separate default networks. A hostname such as `prowlarr` or `rdtclient` resolves from Bookmarkarr only when the containers share a Docker network.

If the external services are already reachable through LAN or reverse-proxy URLs, no network override is needed. Otherwise:

```bash
docker network ls
cp docker-compose.external-networks.example.yml docker-compose.external-networks.yml
```

Set `PROWLARR_DOCKER_NETWORK`, `NZBGET_DOCKER_NETWORK`, and `RDTCLIENT_DOCKER_NETWORK` in `.env` to the existing network names. Remove unused network entries from the copied override, then enable it with:

```dotenv
COMPOSE_FILE=docker-compose.yml:docker-compose.external-networks.yml
```

Validate the merged configuration before starting:

```bash
docker compose config
docker compose up -d
```

The copied override is ignored by Git. The `.example.yml` file contains no server-specific network names, addresses, or credentials.

## Local Developer Build

```bash
docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d --build
```

The override changes only the image source/build configuration. Runtime behavior and mounts remain identical.

## Identity, Permissions, and Time

`PUID`, `PGID`, `UMASK`, and `TZ` control runtime ownership, created-file permissions, and timezone. Ensure all mounted directories are writable by the configured identity. The entrypoint adjusts only Bookmarkarr’s service account/config ownership; it does not recursively take ownership of media mounts.

## Authentication and Public URL

Set `BOOKMARKARR_AUTHENTICATION_REQUIRED=true` for authenticated deployments. `BOOKMARKARR_PUBLIC_URL` is used for external links and notifications. A fixed `BOOKMARKARR_API_KEY` is optional; leaving it empty permits normal runtime configuration. Protect `.env` and never commit it.

## Health and Logs

The image and Compose health checks call `/api/v1/system/ready`. The default JSON-file log rotation is 10 MB times three files and can be changed through `.env`.

```bash
docker compose ps
docker inspect --format '{{.State.Health.Status}}' bookmarkarr
docker compose logs --tail=200 bookmarkarr
```

## Upgrades

Back up config first, pin a semantic image tag when repeatability matters, pull, and recreate only Bookmarkarr:

```bash
docker compose pull bookmarkarr
docker compose up -d --no-deps bookmarkarr
```

Never point Bookmarkarr at another application’s config/database volume.
