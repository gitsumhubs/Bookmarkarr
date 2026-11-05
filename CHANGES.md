# Changes

## 2026-11-05 — codex — Function and performance review fixes (0.1.44)

Why: implement the ten findings from the function/performance review.
Before → after: empty-queue orphans stayed active → confirmed missing downloads retain history as
`SourceMissing`; audiobook-only scheduling → independent edition scheduling and ebook grabs;
inconsistent parent/edition updates → shared audio synchronization with media validation;
entity-based partial edits → omission-aware metadata DTO; silent bulk-delete failures → visible
partial results; inverted realtime cache → first/changed updates and complete snapshots;
historical monitor/queue materialization → targeted queries and shared short-lived reads;
unbounded book listing/ISBN materialization → SQL filters, paginated summaries and SQL ISBN lookup;
request-scoped uptime → process uptime; base-image labels → app version/source revision metadata.
Backup: deployment backups and monitoring before/after records are kept outside the repository;
see the private instance recreation notes for the applied deployment backup location.
Deployment: running image 0.1.42 → 0.1.44 after backup and monitoring alignment; the existing
Compose definition, volume mappings and port remain in use.
Validation: regression coverage for recovery, edition updates/search, snapshots, bulk deletion,
SQL filtering/lookup and monitor candidates. Release/deployment verification is recorded in the
project tracker after publishing.
