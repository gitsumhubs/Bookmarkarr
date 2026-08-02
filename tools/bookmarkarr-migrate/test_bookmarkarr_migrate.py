import importlib.util
import json
import sqlite3
import sys
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace

MODULE_PATH = Path(__file__).with_name("bookmarkarr_migrate.py")
SPEC = importlib.util.spec_from_file_location("bookmarkarr_migrate", MODULE_PATH)
MODULE = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)


class MigrationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        root = Path(self.temp.name)
        self.source, self.destination = root / "source.db", root / "destination.db"
        with sqlite3.connect(self.source) as db:
            db.execute("CREATE TABLE Audiobooks (Id INTEGER PRIMARY KEY, Title TEXT, Authors TEXT, Monitored INTEGER, FilePath TEXT, BasePath TEXT, QualityProfileId INTEGER)")
            db.execute("INSERT INTO Audiobooks VALUES (1, 'A Book', '[\"A Writer\"]', 1, NULL, '/audio/A Book', NULL)")
        with sqlite3.connect(self.destination) as db:
            db.execute("CREATE TABLE Books (Id INTEGER PRIMARY KEY, Title TEXT, Authors TEXT, Monitored INTEGER, FilePath TEXT, BasePath TEXT, QualityProfileId INTEGER)")
            db.execute("CREATE TABLE BookEditions (Id INTEGER PRIMARY KEY, BookId INTEGER, MediaType INTEGER, Monitored INTEGER, UpgradeAllowed INTEGER, Status INTEGER, QualityProfileId INTEGER, RootPath TEXT, DownloadCategory TEXT, CreatedAt TEXT, UpdatedAt TEXT, UNIQUE(BookId,MediaType))")
            db.execute("CREATE TABLE EditionFiles (Id INTEGER PRIMARY KEY, EditionId INTEGER, Path TEXT UNIQUE, Extension TEXT, Size INTEGER, ImportedAt TEXT)")

    def tearDown(self): self.temp.cleanup()

    def test_dry_run_is_read_only_and_commit_is_gated_and_idempotent(self):
        before = MODULE.database_fingerprint(self.source)
        args = SimpleNamespace(source_db=str(self.source), destination_db=str(self.destination), source_ebooks=None)
        plan = MODULE.make_plan(args)
        self.assertTrue(plan.successful)
        self.assertEqual(before, MODULE.database_fingerprint(self.source))
        plan_path = Path(self.temp.name) / "plan.json"
        plan_path.write_text(json.dumps(MODULE.asdict(plan)))
        with self.assertRaises(RuntimeError): MODULE.commit(SimpleNamespace(plan=str(plan_path), confirmation="wrong"))
        commit_args = SimpleNamespace(plan=str(plan_path), confirmation=plan.confirmation_token)
        MODULE.commit(commit_args)
        MODULE.commit(commit_args)
        with sqlite3.connect(self.destination) as db:
            self.assertEqual(1, db.execute("SELECT COUNT(*) FROM Books").fetchone()[0])
            self.assertEqual(1, db.execute("SELECT COUNT(*) FROM BookEditions").fetchone()[0])

    def test_commit_refuses_when_source_changes_after_successful_dry_run(self):
        args = SimpleNamespace(source_db=str(self.source), destination_db=str(self.destination), source_ebooks=None)
        plan = MODULE.make_plan(args)
        plan_path = Path(self.temp.name) / "changed-source-plan.json"
        plan_path.write_text(json.dumps(MODULE.asdict(plan)))

        with sqlite3.connect(self.source) as db:
            db.execute("INSERT INTO Audiobooks VALUES (2, 'Changed', '[\"Writer\"]', 1, NULL, '/audio/Changed', NULL)")

        with self.assertRaisesRegex(RuntimeError, "source database changed"):
            MODULE.commit(SimpleNamespace(plan=str(plan_path), confirmation=plan.confirmation_token))
        with sqlite3.connect(self.destination) as db:
            self.assertEqual(0, db.execute("SELECT COUNT(*) FROM Books").fetchone()[0])

    def test_ebook_discovery_is_conservative_and_source_media_remains_unchanged(self):
        ebook_root = Path(self.temp.name) / "ebooks"
        matched = ebook_root / "A Writer" / "A Book" / "A Book.epub"
        unrelated = ebook_root / "Other Writer" / "Unrelated.epub"
        matched.parent.mkdir(parents=True)
        unrelated.parent.mkdir(parents=True)
        matched.write_bytes(b"matched ebook fixture")
        unrelated.write_bytes(b"unrelated ebook fixture")
        before = {path: (path.read_bytes(), path.stat().st_mtime_ns) for path in (matched, unrelated)}

        args = SimpleNamespace(source_db=str(self.source), destination_db=str(self.destination), source_ebooks=str(ebook_root))
        plan = MODULE.make_plan(args)

        self.assertTrue(plan.successful)
        self.assertEqual([str(matched)], [item["path"] for item in plan.ebook_matches])
        self.assertEqual([str(unrelated)], plan.ebook_unmatched)
        self.assertEqual(before, {path: (path.read_bytes(), path.stat().st_mtime_ns) for path in (matched, unrelated)})

    def test_commit_rolls_back_all_destination_writes_on_failure(self):
        ebook_root = Path(self.temp.name) / "ebooks-rollback"
        matched = ebook_root / "A Writer" / "A Book" / "A Book.epub"
        matched.parent.mkdir(parents=True)
        matched.write_bytes(b"ebook fixture")
        args = SimpleNamespace(source_db=str(self.source), destination_db=str(self.destination), source_ebooks=str(ebook_root))
        plan = MODULE.make_plan(args)
        plan_path = Path(self.temp.name) / "rollback-plan.json"
        plan_path.write_text(json.dumps(MODULE.asdict(plan)))
        with sqlite3.connect(self.destination) as db:
            db.execute("DROP TABLE EditionFiles")

        with self.assertRaises(sqlite3.Error):
            MODULE.commit(SimpleNamespace(plan=str(plan_path), confirmation=plan.confirmation_token))

        with sqlite3.connect(self.destination) as db:
            self.assertEqual(0, db.execute("SELECT COUNT(*) FROM Books").fetchone()[0])
            self.assertEqual(0, db.execute("SELECT COUNT(*) FROM BookEditions").fetchone()[0])


if __name__ == "__main__": unittest.main()
