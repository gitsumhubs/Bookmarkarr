# Security Policy

## Supported Versions

Security fixes are applied to the current semantic release line. Pin a version tag and monitor repository releases when deploying outside a trusted network.

## Reporting a Vulnerability

Use private vulnerability reporting on the eventual GitHub repository or contact the repository owner through a private channel. Do not publish credentials, API keys, filesystem paths, private indexer details, or a working exploit in a public issue. Include affected version, impact, reproduction steps, and suggested mitigation when available.

## Deployment Guidance

- Enable Bookmarkarr authentication and HTTPS before exposing it beyond a trusted LAN/VPN.
- Keep Prowlarr, indexers, download clients, and RDT Client externally secured and non-public.
- Use least-privilege API credentials and rotate them after suspected exposure.
- Run with a non-root `PUID:PGID` and only the required config/media/download mounts.
- Protect `.env`, the config directory, database, logs, and backups.
- Keep the image and dependencies updated and review release changes before upgrading.
- Treat webhooks and download-client URLs as secrets because they may embed tokens.

## Source and License

Bookmarkarr is AGPL-3.0-or-later software. See [LICENSE](LICENSE), [NOTICE](NOTICE), and [ATTRIBUTION.md](ATTRIBUTION.md). Security fixes to a network-deployed modified version remain subject to the AGPL source-availability requirements.
