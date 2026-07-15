#!/usr/bin/env python3
"""Validate canonical PostgreSQL migrations without executing them."""

import hashlib
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
MIGRATIONS = ROOT / "platform/postgres/migrations"
MANIFEST = MIGRATIONS / "manifest.json"
FILENAME = re.compile(r"^(\d{4})_([a-z0-9_]+)\.sql$")
HEADER = (
    re.compile(r"^-- migration-id: (\d{4})$"),
    re.compile(r"^-- owner: ([a-z0-9-]+)$"),
    re.compile(r"^-- description: (.+)$"),
)
FORBIDDEN = {
    "transaction control": re.compile(r"(?im)^\s*(BEGIN|COMMIT|ROLLBACK)\s*;|\\connect\b"),
    "concurrent index": re.compile(r"(?i)\b(?:CREATE|DROP)\s+INDEX\s+CONCURRENTLY\b"),
    "vacuum": re.compile(r"(?im)^\s*VACUUM\b"),
    "destructive DDL": re.compile(
        r"(?i)\bDROP\s+(?:TABLE|SCHEMA|COLUMN)\b|\bTRUNCATE\b|"
        r"\bALTER\s+(?:TABLE\s+\S+\s+)?COLUMN\s+\S+\s+TYPE\b|\bRENAME\s+COLUMN\b"
    ),
}
SECRET = re.compile(r"(?i)(password|service[_-]?key)\s*=\s*['\"][^'\"]+['\"]")


def main() -> int:
    errors: list[str] = []
    files = sorted(MIGRATIONS.glob("*.sql"))
    ids: list[int] = []
    seen: set[str] = set()
    for path in files:
        match = FILENAME.fullmatch(path.name)
        if not match:
            errors.append(f"invalid migration filename: {path.name}")
            continue
        migration_id = match.group(1)
        if migration_id in seen:
            errors.append(f"duplicate migration ID: {migration_id}")
        seen.add(migration_id)
        ids.append(int(migration_id))
        raw = path.read_bytes()
        text = raw.decode("utf-8")
        lines = text.splitlines()
        if len(lines) < 3:
            errors.append(f"{path.name}: missing metadata headers")
            continue
        values = []
        for pattern, line in zip(HEADER, lines[:3], strict=True):
            header_match = pattern.fullmatch(line)
            values.append(header_match.group(1) if header_match else None)
        if None in values:
            errors.append(f"{path.name}: invalid metadata headers")
        elif values[0] != migration_id:
            errors.append(f"{path.name}: header ID {values[0]} differs from filename")
        for label, pattern in FORBIDDEN.items():
            if pattern.search(text):
                errors.append(f"{path.name}: contains forbidden {label}")
        if SECRET.search(text):
            errors.append(f"{path.name}: contains a possible plaintext credential")

    if ids != list(range(1, len(ids) + 1)):
        errors.append("migration IDs must be unique, contiguous, and start at 0001")

    if not MANIFEST.exists():
        errors.append("migration manifest is missing")
    else:
        manifest = json.loads(MANIFEST.read_text())
        recorded = manifest.get("migrations", [])
        actual = [
            {"id": path.name[:4], "filename": path.name, "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}
            for path in files
        ]
        if recorded != actual:
            errors.append("migration manifest does not match exact migration bytes and order")

    k8s_sql = "\n".join(path.read_text(errors="ignore") for path in (ROOT / "k8s").glob("*.yaml"))
    if re.search(r"(?i)\bCREATE\s+(?:TABLE|SCHEMA|VIEW|FUNCTION)\b", k8s_sql):
        errors.append("migration SQL is duplicated in Kubernetes YAML")

    if errors:
        print("migration validation failed:", *errors, sep="\n- ")
        return 1
    print(f"migration validation passed: {len(files)} immutable forward-only migrations")
    return 0


if __name__ == "__main__":
    sys.exit(main())
