"""Validate every committed service and Edge contract with the standard validator."""

import json
from pathlib import Path

from openapi_spec_validator import validate


root = Path(__file__).resolve().parents[1]
contracts = sorted((root / "contracts" / "openapi").glob("*.openapi.json"))
if len(contracts) != 14:
    raise SystemExit(f"expected 14 OpenAPI documents, found {len(contracts)}")

for path in contracts:
    with path.open(encoding="utf-8") as handle:
        validate(json.load(handle))
    print(f"validated {path.name}")
