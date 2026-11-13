# Changes

## 2026-11-13 14:15 EST — claude — Add a "is this the wanted book" check for releases (not yet wired)

Why: unattended grabs pick the best-scored result of whatever a search returned, with no check
that it is the wanted book. That is how an 846 GB "Entire Audiobooks Collection" was grabbed for
one title and twelve-book packs were filed as single books.
Before → after: no such check existed → `ReleaseRelevanceEvaluator` (domain) answers accept or
reject with a reason for a wanted book and a release name: multi-book pack, title not found, one
part of a book, a different volume, a longer title that contains the wanted one, another author.
Not changed: nothing calls it yet. Both automatic search paths still grab as before; wiring it in
is the next step of the same task. No schema, setting or configured path changed; nothing to back up.
Validation: replayed against the 73 real grabs in the live database (12 wrong ones all rejected,
59 correct ones all accepted) and against every one of 115 recorded release names paired with
every one of 102 books (11,621 wrong pairings, none accepted; 100 of 103 correct pairings accepted,
the three misses being "Philosopher's Stone" and "Sorcerer's Stone" asked for under the other name).
On 274 release names fetched fresh from the usenet indexer, the first version accepted one wrong
book (Act I for "The Sandman: Act II"); after that fix, none, with 49 of 54 correct pairings
accepted. Known gaps: a release in another language under the same title is still accepted, and
releases named only by series and number are refused. 1,615 backend tests passed. No request was
sent to AudioBook Bay. Not released or deployed.

## 2026-11-13 02:43 EST — claude — Stop reading ordinary words as language codes

Why: an English release titled "Cul-de-Sac Carnage" was shown as German and scored 15 points lower.
Before → after: any standalone two-letter code in a release title counted as a language whatever
its case, so the hyphen-bounded "de" matched; the usenet scorer also matched "es", "de" and "fr" as
substrings of any word → a language tag must be inside brackets or written in capitals, and
language names match as whole words only. Four separate detectors → one, `ReleaseLanguageDetector`.
An indexer's own language field is still read in any case, and a profile language written as a
code ("en") now accepts the same language reported by name ("English").
Not changed: the patched AudioBook Bay definition still does not carry the site's Language field.
No schema, setting or configured path changed; nothing to back up.
Validation: 1,570 backend tests passed, including new cases for the reported title. Not released or
deployed with this entry; the running image is unchanged until a version is tagged.

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
