#!/usr/bin/env python3
"""Verify that canonical PostgreSQL migrations only append relative to Git history."""

import argparse
import hashlib
import json
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
MIGRATIONS_REL = "platform/postgres/migrations"
MANIFEST_REL = f"{MIGRATIONS_REL}/manifest.json"


def git(*args: str, check: bool = True) -> subprocess.CompletedProcess[bytes]:
    return subprocess.run(
        ["git", *args], cwd=ROOT, check=check, stdout=subprocess.PIPE, stderr=subprocess.PIPE
    )


def load_manifest(raw: bytes, source: str) -> list[dict[str, str]]:
    try:
        document = json.loads(raw)
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise ValueError(f"{source} migration manifest is invalid: {exc}") from exc
    if document.get("format") != 1 or not isinstance(document.get("migrations"), list):
        raise ValueError(f"{source} migration manifest format is invalid")
    entries = document["migrations"]
    for entry in entries:
        if not isinstance(entry, dict) or set(entry) != {"id", "filename", "sha256"}:
            raise ValueError(f"{source} migration manifest entry is invalid")
    return entries


def validate_base_catalog(base_ref: str) -> list[dict[str, str]]:
    raw = git("show", f"{base_ref}:{MANIFEST_REL}").stdout
    entries = load_manifest(raw, "base")
    names = git("ls-tree", "--name-only", f"{base_ref}:{MIGRATIONS_REL}").stdout.decode().splitlines()
    sql_names = sorted(name for name in names if name.endswith(".sql"))
    if len(entries) != len(sql_names):
        raise ValueError("base migration manifest entry count does not match SQL file count")
    actual: list[dict[str, str]] = []
    for name in sql_names:
        file_bytes = git("show", f"{base_ref}:{MIGRATIONS_REL}/{name}").stdout
        lines = file_bytes.decode("utf-8").splitlines()
        migration_id = name[:4]
        if len(lines) < 3 or lines[0] != f"-- migration-id: {migration_id}":
            raise ValueError(f"base migration metadata is invalid: {name}")
        actual.append(
            {"id": migration_id, "filename": name, "sha256": hashlib.sha256(file_bytes).hexdigest()}
        )
    if entries != actual:
        raise ValueError("base migration manifest does not match exact historical bytes and order")
    return entries


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--base-ref", required=True, help="Immutable Git commit used as the append-only base")
    args = parser.parse_args()

    if git("cat-file", "-e", f"{args.base_ref}^{{commit}}", check=False).returncode != 0:
        print(f"migration append-only validation failed: unavailable base ref {args.base_ref}", file=sys.stderr)
        return 1

    current_validation = subprocess.run(
        [sys.executable, str(ROOT / "scripts/validate-migrations.py")], cwd=ROOT
    )
    if current_validation.returncode != 0:
        return current_validation.returncode

    if git("cat-file", "-e", f"{args.base_ref}:{MIGRATIONS_REL}", check=False).returncode != 0:
        print("migration append-only validation passed: initial canonical catalog adoption")
        return 0

    try:
        base = validate_base_catalog(args.base_ref)
        current = load_manifest((ROOT / MANIFEST_REL).read_bytes(), "current")
        if len(current) < len(base):
            raise ValueError("a historical migration was deleted")
        for index, historical in enumerate(base):
            if current[index] != historical:
                raise ValueError(
                    f"historical migration changed at manifest position {index + 1}: {historical['filename']}"
                )
    except (OSError, UnicodeDecodeError, ValueError, subprocess.CalledProcessError) as exc:
        print(f"migration append-only validation failed: {exc}", file=sys.stderr)
        return 1

    print(f"migration append-only validation passed: {len(base)} historical entries preserved")
    return 0


if __name__ == "__main__":
    sys.exit(main())
