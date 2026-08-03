# Goodreads CSV Import

## Workflow

Open Library → Import → Goodreads, select an exported Goodreads CSV, and choose Preview. Every eligible row starts selected with both audiobook and ebook requested. Use select all/none, the bulk format selector, or row checkboxes to change the plan. Resolve every selected ambiguous match before Commit.

Commit adds and monitors books and missing requested editions. When a commit adds a monitored audiobook edition, Bookmarkarr immediately runs the normal automatic search-and-download path for that book after the database transaction commits. Ebook editions are still added and monitored only. Existing edition settings are never overwritten, and a later CSV never removes or unmonitors books absent from that file.

## Matching Order

Rows match in this order:

1. Goodreads Book ID
2. Normalized ISBN-13 or ISBN
3. Normalized exact title plus primary author

One match is accepted, no match creates a book, and multiple exact candidates require manual resolution. Approximate/fuzzy matching is intentionally not used for automatic commits.

## Limits and Parsing

- Maximum upload: 10 MB
- Maximum data rows: 25,000
- UTF-8 with or without BOM
- RFC-style comma quoting, doubled quotes, and quoted newlines
- Goodreads spreadsheet wrappers such as `="978..."`
- Required columns: `Book Id`, `Title`, and either `Author` or `Author l-f`

## Privacy and Expiry

The raw uploaded stream is parsed in memory and never stored. Only normalized fields needed for preview and commit are stored in `CatalogImportBatches`. Preview batches expire after 24 hours, at which point normalized rows are erased. Committed batches erase normalized rows immediately and retain only a compact idempotent summary.

## Reimports

Reimports are additive and idempotent. A commit to the same batch returns its stored summary. Reimporting the same catalog matches existing identifiers and adds only missing selected editions. Existing roots, profiles, categories, monitoring, upgrade settings, and files are not replaced.
