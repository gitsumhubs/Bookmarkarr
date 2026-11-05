# Changes

## 2026-11-05 02:48 EST — codex — Function and performance review fixes (0.1.44)

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
Validation: 1,535 backend tests and 444 frontend tests passed locally and in public release CI;
frontend type/lint checks, four existing migration tests, the backup/monitoring script regression,
Docker build/check, and Compose validation passed. Fresh-config startup and the live LAN library
were checked in a browser; ebook query/grab requests were verified with mocked releases, without
submitting test downloads to live clients. The live monitor confirmed the missing download and
retained its record. Exact instance backup and rollout details are in the private recreation notes
and project tracker.
