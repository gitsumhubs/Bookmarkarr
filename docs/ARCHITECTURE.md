# Bookmarkarr Architecture

## Core Model

`Book` is the public bibliographic aggregate. Internally, the inherited `Audiobook` class remains as a compatibility seam for Listenarr 1.2.2 workflows, but EF maps it to the `Books` table and the API exposes it as a book.

Each book has zero, one, or two `BookEdition` records keyed by `(BookId, MediaType)`. Audiobook and ebook editions independently own monitoring, wanted status, quality profile, root folder/path, category, upgrade permission, last-search time, and registered files. `EditionFile` enforces the media boundary.

## Request and Import Flow

```text
book + requested edition
        |
        v
server derives media type and category
        |
        v
external Prowlarr/Newznab/Torznab search
        |
        v
strict positive media classifier
        |
        v
external NZB/torrent/RDT client
        |
        v
edition-aware queue, retry, and stable processing job
        |
        v
audio files -> audiobook root and registry
ebook files -> ebook root and registry
        |
        v
edition-aware history and notification
```

Unclassified and unrelated releases are rejected. Audio requests allow recognized audio releases and legitimate audio-plus-ebook bundles. Ebook requests reject audio signals and require a recognized ebook signal. A mixed audio bundle can populate the existing ebook edition, but an ebook request never imports audio.

## Layers

- Domain: persistent entities and media/file rules
- Application: orchestration, search filtering, client contracts, queue models
- Infrastructure: SQLite, adapters, import processing, scheduled services, notifications
- API: authenticated REST endpoints, validation, Swagger/OpenAPI, web hosting
- Frontend: edition-aware Vue pages and Goodreads preview/commit UI

## Data Integrity

- Unique `(BookId, MediaType)` prevents duplicate editions.
- Unique `(EditionId, Path)` prevents duplicate file registration.
- Download deduplication includes edition identity where available.
- Processing jobs use stable persisted identifiers and retry state.
- Goodreads commits and source migrations use database transactions.
- Normalized Goodreads batches expire in 24 hours; raw CSV bytes are never persisted.

## Compatibility Improvements

The fork incorporates the improved deployment’s stable job identifiers, strict content filtering, RDT `pausedUP` completion handling, tolerance of absent non-primary sidecars, recoverable retries, AudiobookBay magnet support, ebook-aware importing, and direct Torznab/Newznab operation without the former proxy.
