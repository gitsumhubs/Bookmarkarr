# External Indexer and Download Client Setup

## Deployment Boundary

Bookmarkarr’s Compose file creates only Bookmarkarr. Prowlarr, indexers, NZBGet, SABnzbd, qBittorrent, and RDT Client remain externally managed. Use URLs reachable from inside the Bookmarkarr container.

## Docker Connectivity

Choose one connectivity model:

- Use a LAN or reverse-proxy URL reachable from containers.
- Attach Bookmarkarr to the existing external Docker networks with `docker-compose.external-networks.example.yml`.

Docker service names work only on a shared network. `localhost` inside Bookmarkarr refers to Bookmarkarr itself, not the host or another container. To use the supplied template:

```bash
docker network ls
cp docker-compose.external-networks.example.yml docker-compose.external-networks.yml
```

Set the generic network variables in `.env`, remove unused networks from the copied file, and uncomment:

```dotenv
COMPOSE_FILE=docker-compose.yml:docker-compose.external-networks.yml
```

After `docker compose up -d`, use service URLs such as `http://prowlarr:9696`, `http://nzbget:6789`, or the qBittorrent-compatible URL exposed by RDT Client. Use the actual service names and ports from the external deployment.

## Categories and Roots

Create two categories in every applicable download client:

| Edition | Default category | Container library root |
|---------|------------------|------------------------|
| Audiobook | `audiobooks` | `/audiobooks` |
| Ebook | `ebooks` | `/ebooks` |

Both clients and Bookmarkarr must see completed downloads consistently under `/downloads`, or you must configure remote path mappings. Never map an ebook category to the audiobook root or vice versa. The simplest Docker arrangement mounts the same host download directory at `/downloads` in both containers.

If a client reports a different path, open **Settings → Download Clients → Add Mapping** and enter:

| Field | Generic example |
|------|-----------------|
| Download client | The client that reports the remote path |
| Remote Path | `/client/downloads` |
| Local Path | `/downloads` |

The remote path must match the beginning of the path reported by the client. The local path must be visible inside Bookmarkarr.

## Prowlarr, Newznab, and Torznab

Configure compatible Newznab/Torznab endpoints directly or import them from Prowlarr:

1. Configure AudiobookBay and any other indexers in Prowlarr and confirm they pass Prowlarr's own test. Stock Prowlarr does not ship an AudiobookBay definition, so this requires a Prowlarr build that includes one; Bookmarkarr cannot install indexer definitions into Prowlarr. See [AudiobookBay](#audiobookbay) below.
2. In Bookmarkarr, open **Settings → Indexers → Import from Prowlarr**.
3. Enter a reachable Prowlarr URL, normally `http://prowlarr:9696` on a shared Docker network.
4. Copy the Prowlarr API key from **Prowlarr → Settings → General**.
5. Optionally enter a tag filter to import only explicitly tagged book indexers.
6. Import, then run Bookmarkarr's Test action on each imported indexer.

Use category capabilities that distinguish audio/audiobook and book/ebook. AudiobookBay magnet results are supported directly. The former custom Torznab proxy is not required.

### AudiobookBay

AudiobookBay is not part of Prowlarr's official indexer definitions. A stock Prowlarr install has no AudiobookBay option, and Bookmarkarr cannot add one — indexer definitions live in Prowlarr.

A Prowlarr build that includes the definition is required. One community option is the [prowlarr-abb](https://github.com/BitlessByte0/prowlarr-abb) fork, published as `bitlessbyte/prowlarr`, which is a LinuxServer.io-based image carrying the definition:

```yaml
services:
  prowlarr:
    image: bitlessbyte/prowlarr:latest
    container_name: prowlarr
    environment:
      - PUID=1000
      - PGID=1000
      - TZ=Etc/UTC
    volumes:
      - ./config:/config
    ports:
      - 9696:9696
    restart: unless-stopped
```

This is third-party and unofficial. It inherits upstream Prowlarr releases only when its maintainer rebuilds the image, so `docker compose pull` tracks the fork's release cadence rather than Prowlarr's. Confirm the fork is still actively built before depending on it, and prefer any equivalent build that provides a working definition.

Because AudiobookBay returns magnets, a torrent-capable download client (qBittorrent or RDT Client) must be configured with the `audiobooks` category.

Bookmarkarr applies its own positive server-side classifier after indexer category mapping. Client-supplied media labels cannot widen results.

## NZBGet and SABnzbd

Create separate `audiobooks` and `ebooks` categories and directories in the client. In Bookmarkarr, open **Settings → Download Clients → Add Download Client**, select NZBGet or SABnzbd, and enter a reachable host, port, and credentials. Set the normal category to `audiobooks`; ebook submissions use `ebooks`. Run the Test action before saving.

For NZBGet, use its RPC/Web interface port and RPC username/password. For SABnzbd, use its API key. Bookmarkarr retains stable job references across polls and tolerates absent optional sidecars while requiring a valid primary file.

## qBittorrent

Create separate categories and save paths. Configure the Web UI/API URL and credentials. Ensure paths reported by qBittorrent translate to Bookmarkarr’s `/downloads` mount.

Some qBittorrent-compatible clients represent a single-file torrent as an extension-shaped directory containing an identically named file, such as `Book.m4b/Book.m4b`. Bookmarkarr safely resolves that exact nested file after remote-path translation. It does not recursively select unrelated sibling media from such a directory and rejects reparse-point escape.

## RDT Client

RDT Client exposes a qBittorrent-compatible API. In Bookmarkarr, open **Settings → Download Clients → Add Download Client**, select **qBittorrent**, and enter RDT's reachable API host, port, and credentials. Configure `audiobooks` and `ebooks` categories in RDT, save `audiobooks` as the normal Bookmarkarr category, and add a remote path mapping when RDT and Bookmarkarr report different container paths.

Run the Test action before saving. A torrent in `pausedUP` after completion is considered import-ready. The processing job remains idempotent across retries.

Missing optional covers, cue sheets, and metadata sidecars do not block an available primary audio or ebook file. Missing primary media uses bounded backoff with diagnostic history and becomes manually retryable if attempts are exhausted.

## Testing

Use each Settings connection test first. Then use a legal, small, non-copyrighted fixture to validate search, submission, and import:

1. Confirm audiobook mode contains only audio or legitimate audiobook bundles and ebook mode contains only recognized ebook releases.
2. Send a release and verify the client's category.
3. Watch **Activity** until the client reports completion.
4. Confirm the imported primary file appears under `/audiobooks` or `/ebooks` and in the Bookmarkarr library.
5. If completion does not import, compare the exact client-reported path with `/downloads`, check the mapping, and inspect `docker compose logs --tail=200 bookmarkarr`.

Repository adapter tests are mocked and do not retrieve production content.
