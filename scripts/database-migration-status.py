#!/usr/bin/env python3
"""Compare read-only PostgreSQL history output with the bundled migration manifest."""

import argparse
import json
from pathlib import Path


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--manifest", required=True)
    parser.add_argument("--history", required=True)
    args = parser.parse_args()
    manifest = json.loads(Path(args.manifest).read_text())["migrations"]
    rows = []
    for line in Path(args.history).read_text().splitlines():
        if not line:
            continue
        migration_id, filename, checksum = line.split("|", 2)
        rows.append({"id": migration_id, "filename": filename, "sha256": checksum})
    catalog = {item["id"]: item for item in manifest}
    history = {item["id"]: item for item in rows}
    missing = any(item["id"] not in catalog for item in rows)
    checksum = any(catalog.get(item["id"]) != item for item in rows if item["id"] in catalog)
    highest = max(history, default="0000")
    pending = [item["id"] for item in manifest if item["id"] not in history]
    out_of_order = any(migration_id < highest for migration_id in pending)
    print("history-present: true")
    print("baseline-required: false")
    print(f"current-migration-id: {highest if rows else 'none'}")
    print(f"pending-ids: {','.join(pending) if pending else 'none'}")
    print(f"checksum-mismatch: {str(checksum).lower()}")
    print(f"missing-applied-file: {str(missing).lower()}")
    print(f"out-of-order: {str(out_of_order).lower()}")
    return 2 if missing or checksum or out_of_order else 0


if __name__ == "__main__":
    raise SystemExit(main())
