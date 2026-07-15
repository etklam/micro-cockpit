#!/usr/bin/env python3
"""Stateful mocked tests for immutable Kubernetes database Job attempts."""

import json
import os
import shutil
import subprocess
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
TAG = "0123456789abcdef0123456789abcdef01234567"
IMAGE = f"registry.example.test/project/db-migrator:{TAG}"
NAMESPACE = "micro-cockpit"
SUFFIX = TAG[:12]

MOCK = r'''#!/usr/bin/env python3
import json, os, pathlib, sys, time
state = pathlib.Path(os.environ["KUBE_STATE"])
args = sys.argv[1:]
if args[:2] == ["config", "current-context"]:
    print("test-context"); raise SystemExit(0)
if args[:2] == ["rollout", "status"]:
    raise SystemExit(0)
if args and args[0] == "exec":
    command = " ".join(args)
    if "to_regclass" in command: print(os.environ.get("HISTORY_PRESENT", "f"))
    elif "pg_namespace" in command: print(os.environ.get("MANAGED_SCHEMAS", "t"))
    else: print(os.environ.get("DB_HISTORY", ""), end="")
    raise SystemExit(0)
if args[:2] == ["get", "jobs"]:
    for path in sorted(state.glob("*.json")):
        print(path.stem)
    raise SystemExit(0)
if args[:2] == ["get", "job"]:
    name = args[2]; path = state / f"{name}.json"
    if not path.exists(): raise SystemExit(1)
    document = json.loads(path.read_text())
    output = args[args.index("-o") + 1] if "-o" in args else ""
    if output == "json": print(json.dumps(document))
    elif "Complete" in output:
        print(next((x.get("status", "") for x in document.get("status", {}).get("conditions", []) if x.get("type") == "Complete"), ""), end="")
    elif "Failed" in output:
        print(next((x.get("status", "") for x in document.get("status", {}).get("conditions", []) if x.get("type") == "Failed"), ""), end="")
    raise SystemExit(0)
if args[:2] == ["get", "pods"]: raise SystemExit(0)
if args[:2] == ["create", "-f"]:
    document = json.loads(pathlib.Path(args[2]).read_text())
    target = state / f"{document['metadata']['name']}.json"
    try:
        descriptor = os.open(target, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    except FileExistsError:
        raise SystemExit(1)
    with os.fdopen(descriptor, "w") as handle: json.dump(document, handle)
    time.sleep(float(os.environ.get("CREATE_DELAY", "0")))
    raise SystemExit(0)
if args and args[0] == "wait":
    name = next(value.split("/", 1)[1] for value in args if value.startswith("job/"))
    path = state / f"{name}.json"; document = json.loads(path.read_text())
    if os.environ.get("WAIT_RESULT", "success") == "success":
        document["status"] = {"conditions": [{"type": "Complete", "status": "True"}]}
        path.write_text(json.dumps(document)); raise SystemExit(0)
    document["status"] = {"conditions": [{"type": "Failed", "status": "True"}]}
    path.write_text(json.dumps(document)); raise SystemExit(1)
raise SystemExit(0)
'''


class Harness:
    def __init__(self) -> None:
        self.temp = tempfile.TemporaryDirectory(prefix="k8s-job-lifecycle-")
        self.root = Path(self.temp.name)
        self.state = self.root / "state"
        self.bin = self.root / "bin"
        self.state.mkdir(); self.bin.mkdir()
        kubectl = self.bin / "kubectl"
        kubectl.write_text(MOCK); kubectl.chmod(0o700)

    def close(self) -> None:
        self.temp.cleanup()

    def env(self, **extra: str) -> dict[str, str]:
        return {**os.environ, "PATH": f"{self.bin}:{os.environ['PATH']}", "KUBE_STATE": str(self.state), **extra}

    def render(self, name: str, step: str, status: str = "", backup: str = "") -> Path:
        command = [
            "python3", str(ROOT / "scripts/k8s-database-job.py"), "render", "--name", name,
            "--namespace", NAMESPACE, "--step", step, "--image", IMAGE, "--release-sha", TAG,
        ]
        if backup: command += ["--backup", backup]
        document = json.loads(subprocess.check_output(command, text=True))
        if status:
            document["status"] = {"conditions": [{"type": status, "status": "True"}]}
        path = self.state / f"{name}.json"; path.write_text(json.dumps(document))
        return path

    def upgrade(self, retry: bool = False, expected: int = 0, **env: str) -> subprocess.CompletedProcess[str]:
        command = [str(ROOT / "scripts/run-k8s-database-upgrade.sh"), "--namespace", NAMESPACE,
                   "--image-registry", "registry.example.test/project", "--image-tag", TAG]
        if retry: command.append("--retry-failed-jobs")
        result = subprocess.run(command, env=self.env(**env), text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        if result.returncode != expected:
            raise AssertionError(f"upgrade expected {expected}, got {result.returncode}\n{result.stdout}")
        return result

    def baseline(self, retry: bool = False, expected: int = 0) -> subprocess.CompletedProcess[str]:
        command = [str(ROOT / "scripts/baseline-k8s-database.sh"), "--context", "test-context", "--namespace", NAMESPACE,
                   "--image-registry", "registry.example.test/project", "--image-tag", TAG,
                   "--backup-confirmed", "backup-verified", "--confirm-existing-database"]
        if retry: command.append("--retry-failed-job")
        result = subprocess.run(command, env=self.env(MICRO_COCKPIT_OPERATION_LOCK_NAMESPACE=NAMESPACE),
                                text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        if result.returncode != expected:
            raise AssertionError(f"baseline expected {expected}, got {result.returncode}\n{result.stdout}")
        return result

    def status(self, expected: int = 0, **env: str) -> subprocess.CompletedProcess[str]:
        command = [str(ROOT / "scripts/status-k8s-database.sh"), "--context", "test-context", "--namespace", NAMESPACE,
                   "--image-registry", "registry.example.test/project", "--image-tag", TAG]
        result = subprocess.run(command, env=self.env(**env), text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        if result.returncode != expected:
            raise AssertionError(f"status expected {expected}, got {result.returncode}\n{result.stdout}")
        return result


def main() -> None:
    harness = Harness()
    try:
        harness.upgrade()
        assert len(list(harness.state.glob("*.json"))) == 3
        rendered = "".join(path.read_text() for path in harness.state.glob("*.json"))
        assert "job-spec-sha256" in rendered and "secretKeyRef" in rendered
        assert "test-only-container-password" not in rendered
    finally: harness.close()

    harness = Harness()
    try:
        harness.render(f"db-bootstrap-{SUFFIX}", "bootstrap", "Complete")
        harness.render(f"db-migrate-{SUFFIX}", "migrate")
        harness.upgrade()
        harness.upgrade()
        assert len(list(harness.state.glob("*.json"))) == 3
    finally: harness.close()

    for field in ("image", "step", "hash"):
        harness = Harness()
        try:
            harness.render(f"db-bootstrap-{SUFFIX}", "bootstrap", "Complete")
            path = harness.render(f"db-migrate-{SUFFIX}", "migrate", "Complete")
            document = json.loads(path.read_text())
            if field == "image": document["spec"]["template"]["spec"]["containers"][0]["image"] = "registry.invalid/db-migrator:wrong"
            elif field == "step":
                document["metadata"]["labels"]["micro-cockpit/database-step"] = "finalize"
                document["metadata"]["annotations"]["micro-cockpit/database-step"] = "finalize"
            else: document["metadata"]["annotations"]["micro-cockpit/job-spec-sha256"] = "0" * 64
            path.write_text(json.dumps(document))
            harness.upgrade(expected=1)
        finally: harness.close()

    harness = Harness()
    try:
        harness.render(f"db-bootstrap-{SUFFIX}", "bootstrap", "Complete")
        failed = harness.render(f"db-migrate-{SUFFIX}", "migrate", "Failed")
        harness.upgrade(expected=1)
        assert failed.exists()
        harness.upgrade(retry=True)
        assert (harness.state / f"db-migrate-{SUFFIX}-a2.json").exists()
        harness.upgrade()
    finally: harness.close()

    harness = Harness()
    try:
        harness.render(f"db-bootstrap-{SUFFIX}", "bootstrap", "Complete")
        harness.render(f"db-migrate-{SUFFIX}", "migrate", "Failed")
        harness.render(f"db-migrate-{SUFFIX}-a2", "migrate", "Failed")
        harness.upgrade(retry=True)
        assert (harness.state / f"db-migrate-{SUFFIX}-a3.json").exists()
    finally: harness.close()

    harness = Harness()
    try:
        harness.render(f"db-baseline-{SUFFIX}", "baseline", "Failed", "backup-verified")
        harness.baseline(expected=1)
        harness.baseline(retry=True)
        assert (harness.state / f"db-baseline-{SUFFIX}-a2.json").exists()
    finally: harness.close()

    harness = Harness()
    try:
        harness.render(f"db-bootstrap-{SUFFIX}", "bootstrap", "Complete")
        harness.render(f"db-migrate-{SUFFIX}", "migrate", "Failed")
        harness.render(f"db-finalize-{SUFFIX}", "finalize", "Complete")
        commands = [[str(ROOT / "scripts/run-k8s-database-upgrade.sh"), "--namespace", NAMESPACE,
                     "--image-registry", "registry.example.test/project", "--image-tag", TAG, "--retry-failed-jobs"]] * 2
        processes = [subprocess.Popen(command, env=harness.env(CREATE_DELAY="0.2"), stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True) for command in commands]
        for process in processes: process.communicate(timeout=10)
        assert len(list(harness.state.glob(f"db-migrate-{SUFFIX}-a2.json"))) == 1
        assert not list(harness.state.glob(f"db-migrate-{SUFFIX}-a3.json"))
    finally: harness.close()

    harness = Harness()
    try:
        before = list(harness.state.iterdir())
        result = harness.status(expected=2, HISTORY_PRESENT="f", MANAGED_SCHEMAS="t")
        assert "history-present: false" in result.stdout and "baseline-required: true" in result.stdout
        assert list(harness.state.iterdir()) == before
    finally: harness.close()

    manifest = json.loads((ROOT / "platform/postgres/migrations/manifest.json").read_text())["migrations"]
    history = "\n".join(f"{item['id']}|{item['filename']}|{item['sha256']}" for item in manifest) + "\n"
    harness = Harness()
    try:
        result = harness.status(HISTORY_PRESENT="t", DB_HISTORY=history)
        assert "current-migration-id: 0013" in result.stdout and "pending-ids: none" in result.stdout
        changed = history.replace(manifest[0]["sha256"], "0" * 64, 1)
        mismatch = harness.status(expected=2, HISTORY_PRESENT="t", DB_HISTORY=changed)
        assert "checksum-mismatch: true" in mismatch.stdout
    finally: harness.close()

    print("Mocked Kubernetes database Job lifecycle tests passed.")


if __name__ == "__main__":
    main()
