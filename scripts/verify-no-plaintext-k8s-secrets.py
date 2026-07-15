#!/usr/bin/env python3
"""Fail without printing secret values when tracked Kubernetes files expose credentials."""

from __future__ import annotations

import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SENSITIVE_ENV = re.compile(
    r"^(ConnectionStrings__.+|Auth__LocalRegistrationKey|Internal__ServiceKey|"
    r"POSTGRES_PASSWORD|MIGRATOR_DB_PASSWORD|.+_DB_PASSWORD)$"
)
PLACEHOLDERS = {"", "REQUIRED", "[REDACTED]"}


def tracked_files() -> list[Path]:
    result = subprocess.run(
        ["git", "ls-files", "--cached", "--others", "--exclude-standard", "-z"],
        cwd=ROOT,
        check=True,
        capture_output=True,
    )
    return [
        ROOT / value.decode()
        for value in result.stdout.split(b"\0")
        if value and (ROOT / value.decode()).is_file()
    ]


def historical_compromised_values() -> set[str]:
    values: set[str] = set()

    def record(candidate: str, *, explicit_secret: bool = False) -> None:
        if candidate in PLACEHOLDERS or any(char in candidate for char in "${}[]"):
            return
        if explicit_secret or len(candidate) >= 12:
            values.add(candidate)

    history_paths = [
        "k8s/01a-db-secret.yaml",
        "k8s/01b-app-secret.yaml",
        *[
            path.relative_to(ROOT).as_posix()
            for path in ROOT.glob("services/*/src/*/Program.cs")
        ],
        *[path.relative_to(ROOT).as_posix() for path in ROOT.glob("tests/*.sh")],
        "services/rotation-service/src/TradeDiary.Rotation/appsettings.json",
        "services/rotation-service/src/TradeDiary.Rotation/appsettings.Development.json",
    ]
    commits = subprocess.run(
        ["git", "rev-list", "--all"], cwd=ROOT, check=True, capture_output=True, text=True
    ).stdout.splitlines()
    for commit in commits:
        for path in history_paths:
            result = subprocess.run(
                ["git", "show", f"{commit}:{path}"],
                cwd=ROOT,
                capture_output=True,
                text=True,
            )
            if result.returncode:
                continue
            in_string_data = False
            for line in result.stdout.splitlines():
                if line == "stringData:":
                    in_string_data = True
                    continue
                if in_string_data and line and not line.startswith("  "):
                    in_string_data = False
                if in_string_data:
                    match = re.match(r"^  [A-Z0-9_]+:\s*[\"']?(.+?)[\"']?\s*$", line)
                    if match:
                        record(match.group(1), explicit_secret=True)
            for match in re.finditer(r"Password=([^;\"\s]+)", result.stdout):
                record(match.group(1))
            for pattern in (
                r"X-(?:Service|Registration)-Key:\s*([^'\"\s]+)",
                r'(?:\\?"password\\?"\s*:\s*\\?")([^"\\]+)',
            ):
                for match in re.finditer(pattern, result.stdout):
                    record(match.group(1))
    return values


def validate() -> list[str]:
    errors: list[str] = []
    files = tracked_files()
    for path in files:
        relative = path.relative_to(ROOT).as_posix()
        if relative.startswith("k8s/") and path.suffix in {".yaml", ".yml"}:
            text = path.read_text(encoding="utf-8")
            if re.search(r"(?m)^kind:\s*Secret\s*$", text):
                for match in re.finditer(r"(?ms)^stringData:\s*\n((?:^[ \t]+.*\n?)*)", text):
                    for line in match.group(1).splitlines():
                        item = re.match(r"^\s+[^:#]+:\s*[\"']?(.*?)[\"']?\s*$", line)
                        if item and item.group(1) not in PLACEHOLDERS:
                            errors.append(f"{relative}: usable stringData value")

            lines = text.splitlines()
            for index, line in enumerate(lines):
                env_match = re.match(r"^\s*- name:\s*(\S+)\s*$", line)
                if not env_match or not SENSITIVE_ENV.match(env_match.group(1)):
                    continue
                indent = len(line) - len(line.lstrip())
                block: list[str] = []
                for following in lines[index + 1 :]:
                    following_indent = len(following) - len(following.lstrip())
                    if following.strip() and following_indent <= indent:
                        break
                    block.append(following)
                rendered = "\n".join(block)
                if re.search(r"(?m)^\s+value:\s*", rendered):
                    errors.append(f"{relative}: {env_match.group(1)} uses literal value")
                if "secretKeyRef:" not in rendered:
                    errors.append(f"{relative}: {env_match.group(1)} lacks secretKeyRef")

        if relative == "k8s/secrets.example.env":
            for number, line in enumerate(path.read_text().splitlines(), 1):
                if not line or line.startswith("#"):
                    continue
                if "=" not in line or line.split("=", 1)[1] != "REQUIRED":
                    errors.append(f"{relative}:{number}: example must use REQUIRED")

    compromised = historical_compromised_values()
    if compromised:
        for path in files:
            if path.is_file() and path.stat().st_size < 5_000_000:
                data = path.read_bytes()
                for value in compromised:
                    if value.encode() in data:
                        errors.append(f"{path.relative_to(ROOT)}: known compromised value remains")
                        break
    return errors


if __name__ == "__main__":
    failures = validate()
    if failures:
        print("Plaintext secret validation failed (values redacted):", file=sys.stderr)
        for failure in sorted(set(failures)):
            print(f"- {failure}", file=sys.stderr)
        raise SystemExit(1)
    print("Plaintext Kubernetes secret validation passed.")
