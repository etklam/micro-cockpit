#!/usr/bin/env python3
"""Render and verify immutable Kubernetes database Job specifications."""

import argparse
import hashlib
import json
import sys
from pathlib import Path

PASSWORD_KEYS = [
    "MIGRATOR_DB_PASSWORD",
    "IDENTITY_DB_PASSWORD",
    "JOURNAL_DB_PASSWORD",
    "PERFORMANCE_DB_PASSWORD",
    "DISCIPLINE_DB_PASSWORD",
    "REMINDER_DB_PASSWORD",
    "MARKET_DATA_DB_PASSWORD",
    "PRICE_ALERT_DB_PASSWORD",
    "ROTATION_DB_PASSWORD",
    "STOCK_RESEARCH_DB_PASSWORD",
    "PARTNER_DB_PASSWORD",
    "CONTENT_DB_PASSWORD",
    "TOOL_DB_PASSWORD",
    "OPERATIONS_DB_PASSWORD",
]


def secret_env(name: str, key: str) -> dict:
    return {"name": name, "valueFrom": {"secretKeyRef": {"name": "db-credentials", "key": key}}}


def value_env(name: str, value: str) -> dict:
    return {"name": name, "value": value}


def container(step: str, image: str, release: str, backup: str) -> dict:
    admin = [
        value_env("PGHOST", "postgres"),
        value_env("PGDATABASE", "trade_diary"),
        value_env("PGUSER", "trade_diary"),
        secret_env("PGPASSWORD", "POSTGRES_PASSWORD"),
    ]
    migrator = [
        value_env("PGHOST", "postgres"),
        value_env("PGDATABASE", "trade_diary"),
        value_env("PGUSER", "trade_diary_migrator"),
        secret_env("PGPASSWORD", "MIGRATOR_DB_PASSWORD"),
        value_env("RELEASE_SHA", release),
    ]
    result = {"name": "database-tooling", "image": image, "imagePullPolicy": "Always"}
    if step == "bootstrap":
        result.update(command=["/roles/apply.sh"], args=["bootstrap"], env=admin + [secret_env(key, key) for key in PASSWORD_KEYS])
    elif step == "migrate":
        result.update(args=["migrate"], env=migrator)
    elif step == "finalize":
        result.update(command=["/roles/apply.sh"], args=["finalize"], env=admin)
    elif step == "baseline":
        result.update(args=["baseline", "--confirm-existing-database", "--backup-confirmed", backup], env=migrator)
    else:
        raise ValueError("unsupported database step")
    return result


def security_spec(step: str, image: str, release: str, backup: str) -> dict:
    configured = container(step, image, release, backup)
    secrets = sorted(
        f"{item['name']}:{item['valueFrom']['secretKeyRef']['name']}:{item['valueFrom']['secretKeyRef']['key']}"
        for item in configured["env"]
        if "valueFrom" in item
    )
    user = next(item["value"] for item in configured["env"] if item["name"] == "PGUSER")
    values = sorted(f"{item['name']}:{item['value']}" for item in configured["env"] if "value" in item)
    return {
        "containerName": configured["name"],
        "image": configured["image"],
        "imagePullPolicy": configured["imagePullPolicy"],
        "command": configured.get("command", []),
        "args": configured["args"],
        "databaseIdentity": user,
        "environment": values,
        "secretKeyRefs": secrets,
        "releaseSha": release,
        "databaseStep": step,
        "restartPolicy": "Never",
        "backoffLimit": 0,
    }


def spec_hash(step: str, image: str, release: str, backup: str) -> str:
    encoded = json.dumps(security_spec(step, image, release, backup), sort_keys=True, separators=(",", ":")).encode()
    return hashlib.sha256(encoded).hexdigest()


def render(args: argparse.Namespace) -> int:
    digest = spec_hash(args.step, args.image, args.release_sha, args.backup)
    labels = {
        "micro-cockpit/release-sha": args.release_sha,
        "micro-cockpit/database-step": args.step,
    }
    annotations = {**labels, "micro-cockpit/job-spec-sha256": digest}
    document = {
        "apiVersion": "batch/v1",
        "kind": "Job",
        "metadata": {"name": args.name, "namespace": args.namespace, "labels": labels, "annotations": annotations},
        "spec": {
            "backoffLimit": 0,
            "template": {
                "metadata": {"labels": {"app": args.name, **labels}},
                "spec": {"restartPolicy": "Never", "containers": [container(args.step, args.image, args.release_sha, args.backup)]},
            },
        },
    }
    json.dump(document, sys.stdout, separators=(",", ":"))
    sys.stdout.write("\n")
    return 0


def verify(args: argparse.Namespace) -> int:
    document = json.loads(Path(args.file).read_text())
    expected_hash = spec_hash(args.step, args.image, args.release_sha, args.backup)
    labels = document.get("metadata", {}).get("labels", {})
    annotations = document.get("metadata", {}).get("annotations", {})
    expected_labels = {
        "micro-cockpit/release-sha": args.release_sha,
        "micro-cockpit/database-step": args.step,
    }
    expected_annotations = {
        **expected_labels,
        "micro-cockpit/job-spec-sha256": expected_hash,
    }
    if any(labels.get(key) != value for key, value in expected_labels.items()) or any(
        annotations.get(key) != value for key, value in expected_annotations.items()
    ):
        raise ValueError("metadata identity")
    containers = document.get("spec", {}).get("template", {}).get("spec", {}).get("containers", [])
    if document.get("spec", {}).get("backoffLimit") != 0:
        raise ValueError("backoff limit")
    if document.get("spec", {}).get("template", {}).get("spec", {}).get("restartPolicy") != "Never":
        raise ValueError("restart policy")
    if len(containers) != 1:
        raise ValueError("container count")
    actual = containers[0]
    expected = container(args.step, args.image, args.release_sha, args.backup)
    for field in ("name", "image", "imagePullPolicy", "command", "args"):
        if actual.get(field, []) != expected.get(field, []):
            raise ValueError(field)
    actual_env = actual.get("env", [])
    actual_user = [item.get("value") for item in actual_env if item.get("name") == "PGUSER"]
    expected_user = [item.get("value") for item in expected["env"] if item.get("name") == "PGUSER"]
    if actual_user != expected_user:
        raise ValueError("database identity")
    actual_secrets = sorted(
        f"{item.get('name')}:{item['valueFrom']['secretKeyRef'].get('name')}:{item['valueFrom']['secretKeyRef'].get('key')}"
        for item in actual_env
        if isinstance(item.get("valueFrom", {}).get("secretKeyRef"), dict)
    )
    expected_secrets = security_spec(args.step, args.image, args.release_sha, args.backup)["secretKeyRefs"]
    if actual_secrets != expected_secrets:
        raise ValueError("secret references")
    actual_values = sorted(f"{item.get('name')}:{item.get('value')}" for item in actual_env if "value" in item)
    expected_values = security_spec(args.step, args.image, args.release_sha, args.backup)["environment"]
    if actual_values != expected_values:
        raise ValueError("environment")
    print("verified")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("mode", choices=["render", "verify"])
    parser.add_argument("--step", required=True, choices=["bootstrap", "migrate", "finalize", "baseline"])
    parser.add_argument("--image", required=True)
    parser.add_argument("--release-sha", required=True)
    parser.add_argument("--backup", default="")
    parser.add_argument("--name")
    parser.add_argument("--namespace")
    parser.add_argument("--file")
    args = parser.parse_args()
    try:
        if args.mode == "render":
            if not args.name or not args.namespace:
                parser.error("render requires --name and --namespace")
            return render(args)
        if not args.file:
            parser.error("verify requires --file")
        return verify(args)
    except (OSError, json.JSONDecodeError, KeyError, TypeError, ValueError) as exc:
        print(f"Kubernetes database Job specification mismatch: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
