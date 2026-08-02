# Listenarr Migration

## Safety Contract

The migration utility is never run automatically. It opens the Listenarr source database with SQLite `mode=ro` plus `query_only`, only reads/stat-checks source media, and writes exclusively to the Bookmarkarr destination database and report directory. It never moves, renames, edits, or deletes source media.

A successful dry run is mandatory. Commit requires the generated confirmation token and refuses to run if the source database/WAL fingerprint changes after the plan.

## Preparation

1. Back up both applications.
2. Start Bookmarkarr once so its destination database and migrations exist, then stop only Bookmarkarr for the commit window.
3. Keep the source database and media mounted read-only if practical.
4. Create a report directory outside media roots.

Do not stop or modify Listenarr merely to run a dry run. If Listenarr writes during the review window, create a new dry-run plan before commit.

## Dry Run

```bash
python3 tools/bookmarkarr-migrate/bookmarkarr_migrate.py dry-run \
  --source-db /path/to/listenarr.db \
  --destination-db /path/to/bookmarkarr.db \
  --source-ebooks /path/to/source/ebooks \
  --report-dir ./migration-reports/run-001
```

Review `dry-run-plan.json`, especially errors, warnings, table counts, unmatched ebooks, destination identity, and the confirmation token. Ebooks are discovered only by recognized extensions and conservative exact title/author evidence; ambiguous files are reported and skipped.

## Commit

```bash
python3 tools/bookmarkarr-migrate/bookmarkarr_migrate.py commit \
  --plan ./migration-reports/run-001/dry-run-plan.json \
  --confirmation <token-from-reviewed-plan> \
  --report-dir ./migration-reports/run-001
```

The destination write uses one `BEGIN IMMEDIATE` transaction and rolls back on failure. Inserts are idempotent. The report reconciles source/destination books and counts audiobook editions, ebook editions, and unmatched ebooks.

## Migrated Data

Where schemas support it, the utility copies books, audiobook files, monitoring, identifiers, roots, quality profiles, indexers, API configurations, download clients, remote mappings, settings, author/series state, downloads, history, jobs, processing logs, and move jobs. Each source book gains an audiobook edition. Safely matched ebooks gain an ebook edition and file record.

## Post-Migration Checks

- Review `migration-reconciliation.json` or `migration-error.json`.
- Start Bookmarkarr and verify readiness.
- Validate roots and remote mappings from inside the new container.
- Test each external client without initiating production downloads.
- Inspect unmatched ebook reports and add those manually.
- Keep the source untouched until backups and reconciliation are accepted.
