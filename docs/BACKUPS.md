# Backup and Restore

## What to Back Up

- The complete Bookmarkarr config mount, including `database/bookmarkarr.db`, settings, keys, logs if desired, and cached metadata
- Audiobook and ebook libraries according to your media backup policy
- The untracked deployment `.env` in a protected secrets backup
- Migration reconciliation reports until the migration is accepted

Downloads are staging data and normally do not require backup.

## Consistent Backup

For the simplest consistent SQLite backup, stop only Bookmarkarr, copy/archive the config mount, and restart Bookmarkarr. Do not stop external clients or any unrelated application.

Alternatively use SQLite’s online backup API against `bookmarkarr.db`; copy the main database together with WAL/SHM files only when using a proven SQLite-aware procedure.

## Restore

1. Stop only Bookmarkarr.
2. Preserve the current config mount as a rollback copy.
3. Restore the entire matching config backup with correct `PUID:PGID` access.
4. Start Bookmarkarr and wait for `/api/v1/system/ready`.
5. Reconcile book/edition/file counts and test external-client connectivity.

Never restore a Listenarr database directly over `bookmarkarr.db`; use the documented migration tool.
