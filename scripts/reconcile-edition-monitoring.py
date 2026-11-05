#!/usr/bin/env python3
"""One-time upgrade: preserve legacy audiobook monitoring when editions become authoritative."""
import argparse
import json
from pathlib import Path
import sqlite3


def reconcile(database, backup_dir=None):
    connection = sqlite3.connect(str(database))
    try:
        rows = connection.execute("SELECT e.Id, e.Monitored, b.Monitored FROM BookEditions e JOIN Books b ON b.Id=e.BookId WHERE e.MediaType=0 AND e.Monitored<>b.Monitored").fetchall()
        if backup_dir is not None:
            backup_dir = Path(backup_dir)
            backup_dir.mkdir(parents=True, exist_ok=True)
            backup = backup_dir / "before-edition-monitoring.db"
            # Refuse to replace a prior recovery point.
            if backup.exists():
                raise FileExistsError(backup)
            with sqlite3.connect(str(backup)) as target:
                connection.backup(target)
            (backup_dir / "edition-monitoring-before.json").write_text(json.dumps(rows, indent=2) + "\n")
            with connection:
                connection.executemany("UPDATE BookEditions SET Monitored=? WHERE Id=? AND Monitored=?", [(parent, eid, previous) for eid, previous, parent in rows])
        return {"applied": backup_dir is not None, "audio_editions": len(rows), "changes": [{"editionId": eid, "before": previous, "after": parent} for eid, previous, parent in rows]}
    finally:
        connection.close()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--database", type=Path, required=True)
    parser.add_argument("--apply", action="store_true")
    parser.add_argument("--backup-dir", type=Path)
    args = parser.parse_args()
    if not args.database.is_file():
        parser.error("Database must already exist.")
    if args.apply and args.backup_dir is None:
        parser.error("--apply requires --backup-dir outside the source repository.")
    print(json.dumps(reconcile(args.database, args.backup_dir if args.apply else None), indent=2))
