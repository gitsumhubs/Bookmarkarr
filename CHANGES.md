# Changes

## 2026-11-05 03:07 EST — codex — Preserve active ebooks during recovery (0.1.45)

Why: an ID-based poll can see an active ebook while the same client's category-filtered snapshot
omits it. Before → after: repeated empty category snapshots could outweigh recent progress → a
recent successful client observation protects the record and resets absence evidence. Internal
observation timestamps do not trigger otherwise identical realtime updates.
Build: emulated .NET SDK/Go compilers → native compilers targeting amd64/arm64 using Docker's target
architecture. The superseded 0.1.44 publication job was cancelled; 0.1.45 includes all review fixes.
Backup: the existing pre-upgrade database/config/Compose backup remains the recovery point; this
follow-up changes no schema or configured paths.
Validation: 1,536 backend tests passed, including recent ebook observations and unchanged push
suppression. The native-built amd64 image passed fresh SQLite startup and ebook browser checks.
ARM64 publishing produced the expected Linux runtime target and ARM64 SQLite/gosu ELF binaries.

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
