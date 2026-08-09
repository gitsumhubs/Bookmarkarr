# Troubleshooting

The [README](../README.md#troubleshooting) lists the symptom and the fix. This page explains why,
for the cases where the reason is not obvious.

## A book stays "Missing" after searching

Check the edition has a quality profile on its detail page. Automatic grabs need one; manual
searches don't.

## Ebooks aren't importing

Confirm your client has a separate `ebooks` category pointing at `/downloads/ebooks`, and check the
allowed ebook extensions under **Settings → File Management**.

## Downloads finish but never import

Almost always a path mismatch. Compare the path your client reports with Bookmarkarr's `/downloads`
and add a remote path mapping if they differ.

If Activity reports **Import Blocked** because an older book has no destination, restart the current
Bookmarkarr release once. Startup repair derives the missing per-book path without overwriting
custom paths, and the importer independently derives it from the audiobook edition if a legacy
creation path ever omits it again. Retry the existing import; the files do not need to be
downloaded again.

## "Import Blocked", but the completed download is gone

Import Blocked means Bookmarkarr knows the download completed but could not place any files in the
library.

Use authenticated `POST /api/v1/library/status/reconcile?dryRun=true` to compare these rows with
registered files and fresh download-client snapshots. Review the result, then repeat with
`dryRun=false`. An absent source becomes terminal `Source Missing` history and its edition returns
to **Missing**, making it eligible to search again.

Bookmarkarr never clears the blocked state from a cached, stale, unavailable, or unknown client
snapshot; if the source later reappears, reconciliation restores **Import Blocked** so it can be
retried. Completed, ready, and import-pending handoff rows are checked whatever the edition
currently reads, so stale terminal rows cannot keep it blocked after every exact client source has
disappeared.

## A book sits on "Downloading" forever and is never searched again

A completed handoff whose source has vanished from its client still counts as an *active* download,
and an active download suppresses automatic search for that book indefinitely.

The same reconciliation clears it: the absent handoff becomes `Source Missing` history and the
edition falls back to **Missing**, so automatic search picks it up on the next pass. A handoff the
client still exposes is left strictly alone, and an edition whose import is genuinely in flight
keeps its live status instead of being claimed as **Imported** underneath the running import.

## A release keeps being grabbed and keeps failing

After a release fails to download twice for the same book, Bookmarkarr blocklists it and stops
offering it to automatic search, which then falls through to the next best result.

One failure never blocklists — stalled peers and transient client errors usually clear on a retry.
Import blocks never count either: those mean the download succeeded and Bookmarkarr could not file
it, which is not the release's fault. Blocklisting is per book, so the same release stays available
for other titles.

Review and clear entries with `GET /api/v1/blocklist` and `DELETE /api/v1/blocklist/{id}`, or clear
a whole book with `DELETE /api/v1/blocklist/audiobook/{id}`.

## A usenet download finished but sits in "Import Blocked"

If the release name itself ends in `.m4b`, `.mp3`, or another media extension, NZBGet creates a
destination *directory* with that name, which looks exactly like the qBittorrent single-file
layout. Bookmarkarr recurses for usenet clients and imports the nested payload, ignoring the
`_unpack` copy NZBGet leaves behind so the book is not stored twice.

Single-file layouts like `Book.m4b/Book.m4b` and `Book.epub/Book.epub` from qBittorrent or RDT are
handled automatically when the outer path is mapped correctly.

## The log says a book "already has N audio file(s) on disk"

That is the duplicate guard working. Before grabbing, Bookmarkarr checks the book's destination
directory on disk, not just its own database rows, because files imported before the book was
tracked — or files whose import failed after the download finished — leave no file rows behind and
would otherwise be downloaded a second time.

If the message adds that the files "match no quality rung", Bookmarkarr can see the files but cannot
tell how good they are, so it declines to guess: run a library scan to register them, after which
normal upgrade comparison resumes. Quality inferred from the filename alone always rounds *down*, so
a genuinely better release is still grabbed as an upgrade.

## Bookmarkarr can't reach Prowlarr or the download client

The URL has to resolve from *inside* the container — `localhost` only works if the service is in the
same container. If you address services by name, Bookmarkarr must share a Docker network with them.

Check which networks it joined:

```bash
docker inspect bookmarkarr --format '{{range $k,$v := .NetworkSettings.Networks}}{{$k}} {{end}}'
```

Check a hostname resolves:

```bash
docker exec bookmarkarr getent hosts nzbget
```

No output means Bookmarkarr cannot see it. Revisit the `networks:` block — the name comes from the
folder the other compose file lives in, not the service name, so a Prowlarr in a folder called
`prowlarr-abb` creates `prowlarr-abb_default`. Copy names from `docker network ls` rather than
guessing. Or skip the whole thing and use the LAN URL, `http://192.168.1.50:6789`.

## Something looks unconfigured after upgrading

Check `docker logs bookmarkarr` for warnings about mounts that are missing or not writable.

Bookmarkarr creates defaults on first start **only if missing**, and never overwrites what you have
configured — including the blocklist. Profiles that already exist are never given new blocked words
on upgrade.

## AudiobookBay

See [AudiobookBay](AUDIOBOOKBAY.md) — the nine-result cap, titles with apostrophes, and the 404
served to clients identifying as Prowlarr.
