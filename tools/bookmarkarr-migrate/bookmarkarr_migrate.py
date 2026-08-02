#!/usr/bin/env python3
"""Dry-run-first, transactional Listenarr 1.2.2 to Bookmarkarr migration.

The source database is always opened with SQLite mode=ro. Media discovery only
stats paths and never creates, moves, renames, or deletes source files.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import sqlite3
import sys
from dataclasses import asdict, dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

EBOOK_EXTENSIONS = {".epub", ".mobi", ".azw", ".azw3", ".pdf", ".djvu", ".fb2"}
SHARED_TABLES = [
    "QualityProfiles", "RootFolders", "Indexers", "ApiConfigurations",
    "DownloadClientConfigurations", "RemotePathMappings", "ApplicationSettings",
    "MonitoredAuthors", "MonitoredSeries", "AuthorCacheEntries", "SeriesCacheEntries",
    "AudiobookExternalIdentifiers", "AudiobookSeriesMemberships", "AudiobookFiles",
    "Downloads", "DownloadHistories", "DownloadProcessingJobs", "History",
    "ProcessExecutionLogs", "MoveJobs",
]


@dataclass
class Plan:
    version: int = 1
    created_at: str = field(default_factory=lambda: datetime.now(timezone.utc).isoformat())
    source_db: str = ""
    destination_db: str = ""
    source_sha256: str = ""
    source_size: int = 0
    source_tables: dict[str, int] = field(default_factory=dict)
    destination_tables: list[str] = field(default_factory=list)
    books: int = 0
    audiobook_files: int = 0
    ebook_matches: list[dict[str, Any]] = field(default_factory=list)
    ebook_unmatched: list[str] = field(default_factory=list)
    warnings: list[str] = field(default_factory=list)
    errors: list[str] = field(default_factory=list)
    successful: bool = False
    confirmation_token: str = ""


def digest(path: Path) -> str:
    value = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            value.update(chunk)
    return value.hexdigest()


def database_fingerprint(path: Path) -> str:
    """Fingerprint the database and any live WAL without writing a checkpoint."""
    value = hashlib.sha256()
    for candidate in (path, Path(str(path) + "-wal")):
        if candidate.exists():
            value.update(candidate.name.encode())
            value.update(bytes.fromhex(digest(candidate)))
    return value.hexdigest()


def connect_readonly(path: Path) -> sqlite3.Connection:
    uri = f"file:{path.resolve().as_posix()}?mode=ro"
    connection = sqlite3.connect(uri, uri=True)
    connection.row_factory = sqlite3.Row
    connection.execute("PRAGMA query_only=ON")
    return connection


def tables(connection: sqlite3.Connection) -> set[str]:
    return {row[0] for row in connection.execute("SELECT name FROM sqlite_master WHERE type='table'")}


def columns(connection: sqlite3.Connection, table: str) -> list[str]:
    return [row[1] for row in connection.execute(f'PRAGMA table_info("{table}")')]


def normalize(value: str | None) -> str:
    return " ".join(re.sub(r"[^a-z0-9 ]+", " ", (value or "").lower()).split())


def json_first(value: str | None) -> str:
    try:
        parsed = json.loads(value or "[]")
        return str(parsed[0]) if isinstance(parsed, list) and parsed else ""
    except (ValueError, TypeError):
        return ""


def discover_ebooks(root: Path | None, source: sqlite3.Connection, book_table: str) -> tuple[list[dict[str, Any]], list[str]]:
    if root is None or not root.exists():
        return [], []
    book_columns = set(columns(source, book_table))
    wanted = [name for name in ["Id", "Title", "Authors"] if name in book_columns]
    if "Id" not in wanted or "Title" not in wanted:
        return [], ["Book table lacks Id or Title; ebook matching skipped"]
    books = [dict(row) for row in source.execute(f'SELECT {",".join(wanted)} FROM "{book_table}"')]
    matches: list[dict[str, Any]] = []
    unmatched: list[str] = []
    for path in sorted(p for p in root.rglob("*") if p.is_file() and p.suffix.lower() in EBOOK_EXTENSIONS):
        stem = normalize(path.stem)
        candidates = []
        for book in books:
            title = normalize(book.get("Title"))
            author = normalize(json_first(book.get("Authors")))
            parent = normalize(" ".join(path.parts[-4:-1]))
            if stem == title or (title and title in stem and (not author or author in parent or author in stem)):
                candidates.append(book)
        if len(candidates) == 1:
            matches.append({"sourceBookId": candidates[0]["Id"], "path": str(path), "size": path.stat().st_size})
        else:
            unmatched.append(str(path))
    return matches, unmatched


def make_plan(args: argparse.Namespace) -> Plan:
    source_path = Path(args.source_db).resolve()
    destination_path = Path(args.destination_db).resolve()
    plan = Plan(source_db=str(source_path), destination_db=str(destination_path))
    if not source_path.is_file():
        plan.errors.append("Source database does not exist")
        return plan
    if not destination_path.is_file():
        plan.errors.append("Destination database must be initialized by Bookmarkarr before dry-run")
        return plan
    plan.source_sha256 = database_fingerprint(source_path)
    plan.source_size = source_path.stat().st_size
    try:
        with connect_readonly(source_path) as source, connect_readonly(destination_path) as destination:
            source_tables = tables(source)
            destination_tables = tables(destination)
            plan.destination_tables = sorted(destination_tables)
            book_table = "Audiobooks" if "Audiobooks" in source_tables else "Books" if "Books" in source_tables else ""
            if not book_table:
                plan.errors.append("Source has neither Audiobooks nor Books table")
            if "Books" not in destination_tables or "BookEditions" not in destination_tables:
                plan.errors.append("Destination is not a migrated Bookmarkarr database")
            for table in sorted(source_tables):
                if table.startswith("sqlite_") or table == "__EFMigrationsHistory":
                    continue
                plan.source_tables[table] = source.execute(f'SELECT COUNT(*) FROM "{table}"').fetchone()[0]
            if book_table:
                plan.books = plan.source_tables.get(book_table, 0)
                plan.audiobook_files = plan.source_tables.get("AudiobookFiles", 0)
                matches, unmatched = discover_ebooks(Path(args.source_ebooks).resolve() if args.source_ebooks else None, source, book_table)
                plan.ebook_matches = matches
                plan.ebook_unmatched = unmatched
                if unmatched:
                    plan.warnings.append(f"{len(unmatched)} ebook files were not exact safe matches and will not be migrated")
            for table in SHARED_TABLES:
                if table in source_tables and table not in destination_tables:
                    plan.warnings.append(f"Destination lacks optional table {table}; it will be skipped")
    except (sqlite3.Error, OSError) as exc:
        plan.errors.append(str(exc))
    plan.successful = not plan.errors
    token_material = json.dumps({k: v for k, v in asdict(plan).items() if k != "confirmation_token"}, sort_keys=True).encode()
    plan.confirmation_token = hashlib.sha256(token_material).hexdigest()[:24]
    return plan


def copy_table(source: sqlite3.Connection, destination: sqlite3.Connection, source_table: str, destination_table: str) -> int:
    source_cols = columns(source, source_table)
    destination_cols = set(columns(destination, destination_table))
    common = [column for column in source_cols if column in destination_cols]
    if not common:
        return 0
    names = ",".join(f'"{name}"' for name in common)
    placeholders = ",".join("?" for _ in common)
    before = destination.total_changes
    for row in source.execute(f'SELECT {names} FROM "{source_table}"'):
        destination.execute(f'INSERT OR IGNORE INTO "{destination_table}" ({names}) VALUES ({placeholders})', tuple(row))
    return destination.total_changes - before


def commit(args: argparse.Namespace) -> dict[str, Any]:
    plan_data = json.loads(Path(args.plan).read_text(encoding="utf-8"))
    plan = Plan(**plan_data)
    if not plan.successful:
        raise RuntimeError("Commit refused: dry-run plan was not successful")
    if args.confirmation != plan.confirmation_token:
        raise RuntimeError("Commit refused: confirmation token does not match the successful dry-run")
    source_path = Path(plan.source_db)
    destination_path = Path(plan.destination_db)
    if database_fingerprint(source_path) != plan.source_sha256:
        raise RuntimeError("Commit refused: source database changed after dry-run")

    report: dict[str, Any] = {"startedAt": datetime.now(timezone.utc).isoformat(), "copied": {}, "errors": [], "reconciliation": {}}
    with connect_readonly(source_path) as source, sqlite3.connect(destination_path) as destination:
        destination.row_factory = sqlite3.Row
        destination.execute("PRAGMA foreign_keys=ON")
        destination.execute("BEGIN IMMEDIATE")
        try:
            source_tables = tables(source)
            destination_tables = tables(destination)
            source_books = "Audiobooks" if "Audiobooks" in source_tables else "Books"
            report["copied"]["Books"] = copy_table(source, destination, source_books, "Books")
            for table in SHARED_TABLES:
                if table in source_tables and table in destination_tables:
                    report["copied"][table] = copy_table(source, destination, table, table)

            # Add one independent audiobook edition per migrated book, preserving legacy settings.
            destination.execute("""
                INSERT OR IGNORE INTO BookEditions
                    (BookId, MediaType, Monitored, UpgradeAllowed, Status, QualityProfileId, RootPath, DownloadCategory, CreatedAt, UpdatedAt)
                SELECT Id, 0, Monitored, 1,
                       CASE WHEN Monitored=0 THEN 0 WHEN FilePath IS NULL AND BasePath IS NULL THEN 1 ELSE 4 END,
                       QualityProfileId, BasePath, 'audiobooks', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
                FROM Books
            """)
            for item in plan.ebook_matches:
                book_id = item["sourceBookId"]
                destination.execute("""
                    INSERT OR IGNORE INTO BookEditions
                        (BookId, MediaType, Monitored, UpgradeAllowed, Status, RootPath, DownloadCategory, CreatedAt, UpdatedAt)
                    VALUES (?, 1, 1, 1, 4, ?, 'ebooks', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
                """, (book_id, str(Path(item["path"]).parent)))
                edition = destination.execute("SELECT Id FROM BookEditions WHERE BookId=? AND MediaType=1", (book_id,)).fetchone()
                if edition:
                    destination.execute("""
                        INSERT OR IGNORE INTO EditionFiles (EditionId, Path, Extension, Size, ImportedAt)
                        VALUES (?, ?, ?, ?, CURRENT_TIMESTAMP)
                    """, (edition[0], item["path"], Path(item["path"]).suffix.lower(), item["size"]))

            destination.commit()
            report["completedAt"] = datetime.now(timezone.utc).isoformat()
            report["reconciliation"] = {
                "sourceBooks": plan.books,
                "destinationBooks": destination.execute("SELECT COUNT(*) FROM Books").fetchone()[0],
                "audiobookEditions": destination.execute("SELECT COUNT(*) FROM BookEditions WHERE MediaType=0").fetchone()[0],
                "ebookEditions": destination.execute("SELECT COUNT(*) FROM BookEditions WHERE MediaType=1").fetchone()[0],
                "unmatchedEbooks": len(plan.ebook_unmatched),
            }
        except Exception:
            destination.rollback()
            raise
    return report


def write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    dry = commands.add_parser("dry-run")
    dry.add_argument("--source-db", required=True)
    dry.add_argument("--destination-db", required=True)
    dry.add_argument("--source-ebooks")
    dry.add_argument("--report-dir", required=True)
    apply = commands.add_parser("commit")
    apply.add_argument("--plan", required=True)
    apply.add_argument("--confirmation", required=True)
    apply.add_argument("--report-dir", required=True)
    args = parser.parse_args()
    try:
        if args.command == "dry-run":
            plan = make_plan(args)
            output = Path(args.report_dir) / "dry-run-plan.json"
            write_json(output, asdict(plan))
            print(json.dumps({"successful": plan.successful, "plan": str(output), "confirmationToken": plan.confirmation_token}, indent=2))
            return 0 if plan.successful else 2
        report = commit(args)
        output = Path(args.report_dir) / "migration-reconciliation.json"
        write_json(output, report)
        print(json.dumps({"successful": True, "report": str(output), "reconciliation": report["reconciliation"]}, indent=2))
        return 0
    except Exception as exc:
        error = {"successful": False, "error": str(exc), "at": datetime.now(timezone.utc).isoformat()}
        write_json(Path(args.report_dir) / "migration-error.json", error)
        print(json.dumps(error, indent=2), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
