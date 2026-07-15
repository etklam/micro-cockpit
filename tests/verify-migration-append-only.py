#!/usr/bin/env python3
"""Exercise append-only migration validation against isolated temporary Git histories."""

import hashlib
import json
import shutil
import subprocess
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def run(repo: Path, *args: str, expected: int = 0) -> None:
    result = subprocess.run(args, cwd=repo, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    if result.returncode != expected:
        raise AssertionError(f"expected {expected}, got {result.returncode}: {' '.join(args)}\n{result.stdout}")


def migration(path: Path, migration_id: int, suffix: str | None = None) -> None:
    ident = f"{migration_id:04d}"
    name = f"{ident}_{suffix or f'test_{ident}'}.sql"
    path.joinpath(name).write_text(
        f"-- migration-id: {ident}\n-- owner: journal-service\n-- description: Test {ident}\n\nSELECT {migration_id};\n"
    )


def manifest(repo: Path) -> None:
    directory = repo / "platform/postgres/migrations"
    entries = [
        {"id": path.name[:4], "filename": path.name, "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}
        for path in sorted(directory.glob("*.sql"))
    ]
    directory.joinpath("manifest.json").write_text(json.dumps({"format": 1, "migrations": entries}, indent=2) + "\n")


def repository(with_catalog: bool = True) -> tuple[tempfile.TemporaryDirectory[str], Path, str]:
    temporary = tempfile.TemporaryDirectory(prefix="migration-history-")
    repo = Path(temporary.name)
    (repo / "scripts").mkdir()
    (repo / "k8s").mkdir()
    shutil.copy(ROOT / "scripts/validate-migrations.py", repo / "scripts")
    shutil.copy(ROOT / "scripts/validate-migration-append-only.py", repo / "scripts")
    if with_catalog:
        directory = repo / "platform/postgres/migrations"
        directory.mkdir(parents=True)
        migration(directory, 1)
        migration(directory, 2)
        manifest(repo)
    run(repo, "git", "init", "-q")
    run(repo, "git", "config", "user.email", "migration-test@example.invalid")
    run(repo, "git", "config", "user.name", "Migration Test")
    run(repo, "git", "add", ".")
    run(repo, "git", "commit", "-qm", "base")
    base = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=repo, text=True).strip()
    return temporary, repo, base


def validate(repo: Path, base: str, expected: int) -> None:
    run(repo, "python3", "scripts/validate-migration-append-only.py", "--base-ref", base, expected=expected)


def main() -> None:
    temporary, repo, base = repository()
    with temporary:
        migration(repo / "platform/postgres/migrations", 3)
        manifest(repo)
        validate(repo, base, 0)

    for mutation in ("edit", "rename", "delete", "reorder", "lower"):
        temporary, repo, base = repository()
        with temporary:
            directory = repo / "platform/postgres/migrations"
            if mutation == "edit":
                target = directory / "0001_test_0001.sql"
                target.write_text(target.read_text() + "-- changed\n")
                manifest(repo)
            elif mutation == "rename":
                (directory / "0001_test_0001.sql").rename(directory / "0001_renamed.sql")
                manifest(repo)
            elif mutation == "delete":
                (directory / "0002_test_0002.sql").unlink()
                manifest(repo)
            elif mutation == "reorder":
                document = json.loads((directory / "manifest.json").read_text())
                document["migrations"].reverse()
                (directory / "manifest.json").write_text(json.dumps(document))
            else:
                migration(directory, 0)
                manifest(repo)
            validate(repo, base, 1)

    temporary, repo, base = repository()
    with temporary:
        validate(repo, "not-a-real-ref", 1)

    temporary, repo, base = repository(with_catalog=False)
    with temporary:
        directory = repo / "platform/postgres/migrations"
        directory.mkdir(parents=True)
        migration(directory, 1)
        manifest(repo)
        validate(repo, base, 0)

    print("Migration Git-history append-only tests passed.")


if __name__ == "__main__":
    main()
