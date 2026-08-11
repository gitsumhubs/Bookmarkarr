# AudiobookBay

AudiobookBay works through Prowlarr, and its magnet results work directly. Two things about it
are unusual enough to need explaining: getting the definition into Prowlarr at all, and the
nine-result cap.

## Prowlarr needs a build that ships the definition

Stock Prowlarr does not include AudiobookBay — it isn't in the official indexer definitions, and
Bookmarkarr cannot install definitions into Prowlarr for you.

One community option is the [prowlarr-abb](https://github.com/BitlessByte0/prowlarr-abb) fork
(`bitlessbyte/prowlarr`), a LinuxServer.io-based image with the definition included. It is
third-party and unofficial: it only picks up upstream Prowlarr releases when its maintainer
rebuilds, so check it is still actively maintained before depending on it. Any build providing a
working AudiobookBay definition will do.

There is a second reason the fork matters. **AudioBook Bay serves a 404 to clients identifying as
Prowlarr.** On a stock Prowlarr the indexer installs cleanly, tests healthy, and returns nothing —
a failure that looks like a Bookmarkarr fault. Forks substitute a different User-Agent. **Run
search test** in **Settings → ABB Patch** tells you which situation you are in.

## Setting it up

1. In Prowlarr, add AudiobookBay and confirm it passes Prowlarr's own **Test**.
2. Check its category mappings distinguish audio/audiobook from book/ebook — Bookmarkarr uses
   those to route results.
3. In Bookmarkarr, **Settings → Indexers → Import from Prowlarr**, then **Test**.
4. AudiobookBay returns **magnets**, so you need a torrent-capable client — qBittorrent or RDT
   Client — configured with the `audiobooks` category.

Bookmarkarr filters results server-side, so an audiobook search only returns audiobooks and an
ebook search only returns ebooks.

## The nine-result cap, and the patch

Prowlarr's compiled AudioBook Bay indexer reads only the first page of the site's results, so
every query is capped at nine no matter how many the site holds. Nothing reports the shortfall.

**Settings → ABB Patch** replaces that indexer with a paginated Cardigann definition: it writes
the definition into Prowlarr's `Definitions/Custom/`, waits for Prowlarr to reload it from disk
(no restart), creates the indexer over Prowlarr's API, and repoints Bookmarkarr's own indexer
record at it. Three pages by default — up to 27 results per search.

The tab checks each precondition separately, so a failure names itself: Prowlarr connection saved,
Prowlarr reachable, AudioBook Bay indexer in use, definitions directory found and writable,
definition installed, site answering searches.

### Pacing

AudioBook Bay bans an IP for about a week when asked too often, so Bookmarkarr searches it slowly
and deliberately. Nothing here is configurable — the values are the point.

- **One book at a time.** While any torrent indexer is enabled, the automatic pass, a catalogue
  import, a retry after a failed download, and a person clicking **Search & Download** all queue
  behind each other rather than running together.
- **At least sixty seconds between them**, plus a random slice of up to thirty on top — never less
  than the minute. The gap is armed when a book finishes, however it finished, and again after each
  individual torrent request. That caps the site at sixty requests an hour no matter how many books
  are waiting, which the hourly budget alone cannot do: it bounds the total but not the rate, so an
  hour's worth could be spent in ten minutes and still sit inside it. The randomness is there
  because a perfectly fixed interval solves bursts and replaces them with a metronome.
- **Twelve seconds between page fetches.** A paginated search is several requests; the definition's
  `requestDelay` spreads a three-page search over about half a minute instead of firing it inside
  six seconds. This one is not randomised — Cardigann takes a single number, not a range.

Usenet indexers are exempt from all of it — they are not what bans. A manual search therefore fills
in twice: NZB results appear at once, and torrent results join them as each torrent search reaches
the front of the queue. A search that looks stuck is usually waiting its turn; the log says so.

A usenet-only install is not paced at all.

### The mount

Prowlarr exposes no API for installing a definition, so the file has to be written to disk — which
means Prowlarr's config directory has to be visible to Bookmarkarr at `/prowlarr-config`. The
Compose example ships that mount already present, pointing at an empty local folder. Point it at
Prowlarr's own config directory instead — the one holding `config.xml`:

```yaml
- /path/to/prowlarr/config:/prowlarr-config
```

Using the repository's `docker-compose.yml` with an `.env` file, the same thing is
`BOOKMARKARR_PROWLARR_CONFIG_PATH=/path/to/prowlarr/config`.

Then `docker compose up -d`. A volume cannot be added to a running container, so this recreates it.

`/prowlarr` and `/config/prowlarr` are checked too, and any other path can be typed into the tab
and verified before use. The tab distinguishes "not mounted" from "mounted, but that isn't
Prowlarr's config directory", because the fix differs.

### If Prowlarr is on another host

It cannot be mounted at all. The tab shows the definition to copy into
`<prowlarr-config>/Definitions/Custom/audiobookbay.yml` yourself; **I've installed it — wire it
up** then does the rest over Prowlarr's API.

### Connection without import

The patch needs Bookmarkarr to know where Prowlarr is. If no connection is saved, the tab asks for
the URL and API key itself and stores them on **Test and save**, importing nothing. The same is
available as **Test & save connection** in **Settings → Indexers**.

Once the patch is applied, importing from Prowlarr skips its compiled AudioBook Bay indexer rather
than adding the nine-result one back alongside the patched one.

## Titles with apostrophes

Prowlarr's AudioBook Bay indexer requests `?s=<terms>&tt=1`, a title-only search matching whole
words — and it replaces the apostrophe with a space on the way, so `The Sailor's Compass` is sent
as `the sailor s compass`. Nothing matches: not that form, not `Sailors`, not the bare stem
`Sailor`. The site's own search finds the release immediately, which makes this look like a
Bookmarkarr fault when it isn't.

Bookmarkarr works around it by dropping apostrophe-bearing words from a later rung of its search
ladder, so the title is also tried as `The Compass` plus the author. If you are debugging this
yourself, test against the site directly rather than through Prowlarr — every spelling fails
identically through Prowlarr, which hides the cause.
