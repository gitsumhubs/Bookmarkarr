"""Regression coverage for the opt-in monitoring upgrade, using a disposable SQLite database."""
import importlib.util
from pathlib import Path
import sqlite3
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("reconcile", Path(__file__).with_name("reconcile-edition-monitoring.py"))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class ReconcileMonitoringTests(unittest.TestCase):
    def test_preview_then_apply_preserves_ebooks_and_original_backup(self):
        with tempfile.TemporaryDirectory() as directory:
            database = Path(directory) / "test.db"
            with sqlite3.connect(database) as db:
                db.executescript("CREATE TABLE Books(Id INTEGER, Monitored INTEGER); CREATE TABLE BookEditions(Id INTEGER, BookId INTEGER, MediaType INTEGER, Monitored INTEGER); INSERT INTO Books VALUES(1,0); INSERT INTO BookEditions VALUES(1,1,0,1),(2,1,1,1);")
            preview = module.reconcile(database)
            self.assertFalse(preview["applied"])
            self.assertEqual(1, preview["audio_editions"])
            backup = Path(directory) / "backups"
            applied = module.reconcile(database, backup)
            self.assertTrue(applied["applied"])
            with sqlite3.connect(database) as db:
                self.assertEqual([(1, 0), (2, 1)], db.execute("SELECT Id,Monitored FROM BookEditions ORDER BY Id").fetchall())
            with sqlite3.connect(backup / "before-edition-monitoring.db") as db:
                self.assertEqual([(1, 1), (2, 1)], db.execute("SELECT Id,Monitored FROM BookEditions ORDER BY Id").fetchall())
            self.assertEqual(0, module.reconcile(database)["audio_editions"])
            with self.assertRaises(FileExistsError):
                module.reconcile(database, backup)
