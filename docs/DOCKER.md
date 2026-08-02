# Docker Deployment

## Normal Deployment

Normal deployment pulls the public GHCR image and runs one service. The repository and source toolchain are not required, no image build occurs, and no integration containers are bundled.

```bash
mkdir -p bookmarkarr/data/config bookmarkarr/data/audiobooks bookmarkarr/data/ebooks bookmarkarr/data/downloads
cd bookmarkarr
curl -fsSLO https://raw.githubusercontent.com/gitsumhubs/Bookmarkarr/main/docker-compose.yml
docker compose pull
docker compose up -d
docker compose ps
```

The downloaded `docker-compose.yml` contains `image: ghcr.io/gitsumhubs/bookmarkarr:latest` and no `build:` configuration. The four mounts isolate mutable configuration/database, audiobook media, ebook media, and download staging. Relative defaults make the file runnable as-is. Edit the host side of those volume entries for existing libraries, or use an optional `.env` file to override the checked-in defaults.

## External Docker Networks

Services in separate Compose projects normally occupy separate default networks. A hostname such as `prowlarr` or `rdtclient` resolves from Bookmarkarr only when the containers share a Docker network.

If the external services are already reachable through LAN or reverse-proxy URLs, no Compose change is needed. Otherwise, add each existing network to the same `docker-compose.yml`. For one shared external network:

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

Find the real network name and validate the edited file before starting:

```bash
docker network ls
docker compose config
docker compose up -d
```

Add more external network entries when Prowlarr and download clients live in different Compose projects. A source checkout also includes `docker-compose.external-networks.example.yml` as an optional multi-file template, but it is not required for normal deployment.

## Local Developer Build

```bash
docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d --build
```

This source-development override is not used by normal deployments. Runtime behavior and mounts remain identical.

## Identity, Permissions, and Time

`PUID`, `PGID`, `UMASK`, and `TZ` control runtime ownership, created-file permissions, and timezone. Ensure all mounted directories are writable by the configured identity. The entrypoint adjusts only Bookmarkarr’s service account/config ownership; it does not recursively take ownership of media mounts.

## Authentication and Public URL

Set `BOOKMARKARR_AUTHENTICATION_REQUIRED=true` for authenticated deployments. `BOOKMARKARR_PUBLIC_URL` is used for external links and notifications. A fixed `BOOKMARKARR_API_KEY` is optional; leaving it empty permits normal runtime configuration. Put overrides directly in Compose or in an optional `.env`; protect `.env` and never commit it.

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
