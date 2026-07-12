#!/usr/bin/env python3
"""Prove an independently implemented fixture exposes the committed Tool contract."""

import importlib.util
import json
from pathlib import Path

root = Path(__file__).resolve().parents[1]
fixture = root / "tests/fixtures/replacement-tool-service.py"
spec = importlib.util.spec_from_file_location("replacement", fixture)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
contract = json.loads((root / "contracts/openapi/tool-service.openapi.json").read_text())
expected = {(method.upper(), path) for path, item in contract["paths"].items() for method in item if method in {"get", "post", "put", "patch", "delete"}}
assert module.ROUTES == expected, f"replacement route drift: missing={expected-module.ROUTES}, extra={module.ROUTES-expected}"

for method, path in sorted(expected):
    status, body = module.dispatch(method, path, {})
    assert status == 200 and isinstance(body, dict), f"{method} {path} failed"

print(f"rewrite contract verified: independent Python replacement, {len(expected)} operations")
