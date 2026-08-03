# Bookmarkarr API

## Access

The versioned base path is `/api/v1`. Interactive requests use the existing session and antiforgery flow. Automation uses the configured API key according to the global API security policy. Goodreads and edition mutation routes are not anonymous.

Swagger/OpenAPI metadata is served by the application and is branded Bookmarkarr.

## Books and Editions

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/api/v1/books` | List unified books; optional `mediaType` and `wanted` filters |
| GET | `/api/v1/books/{id}` | Get a book and all edition/file state |
| PATCH | `/api/v1/books/editions/{editionId}` | Update monitoring, quality, root, category, upgrade, or status |
| POST | `/api/v1/books/editions/bulk` | Apply one update to edition IDs |
| POST | `/api/v1/books/editions/{editionId}/search` | Search only the requested edition using server-derived media type |
| POST | `/api/v1/books/editions/{editionId}/files` | Register a file only when its extension matches the edition |

`EditionMediaType` values are `Audiobook` and `Ebook`. `EditionWantedStatus` values are `Unmonitored`, `Missing`, `Queued`, `Downloading`, `Imported`, and `UpgradeAvailable`.

## Goodreads

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/api/v1/catalog-imports/goodreads/preview` | Multipart CSV upload and normalized preview |
| GET | `/api/v1/catalog-imports/goodreads/{batchId}` | Retrieve a live normalized preview batch |
| POST | `/api/v1/catalog-imports/goodreads/{batchId}/commit` | Transactionally commit selected/resolved rows |

Preview is `multipart/form-data` with a `file` part. Commit contains `rows`, each with `rowId`, `selected`, `mediaFormats`, and optional `resolvedBookId`. See [GOODREADS.md](GOODREADS.md).
When a commit adds a monitored audiobook edition, Bookmarkarr immediately runs the existing automatic search-and-download path for that book as a best-effort follow-up after the transaction commits.

## Edition-Aware Operational Data

Downloads, queue items, history, library list items, and SignalR payloads include edition identity/media type where applicable. Existing v1 audiobook endpoints remain available as compatibility routes and target the audiobook edition.

## Validation

- Unsupported file extensions return `400`.
- Missing batches return `404`; expired batches return `410`.
- Unresolved ambiguity returns `409`.
- Oversized Goodreads payloads return `413`.
- Search content type is derived from the edition record, not trusted from client input.
