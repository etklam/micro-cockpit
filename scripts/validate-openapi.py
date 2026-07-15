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


def security_for(document: dict, path: str, method: str) -> set[str]:
    operation = document["paths"][path][method.lower()]
    return {name for requirement in operation.get("security", []) for name in requirement}


documents = {path.name: json.loads(path.read_text(encoding="utf-8")) for path in contracts}
expected = {
    ("reminder-service.openapi.json", "/internal/events/diary-deleted", "post"): {"serviceKey"},
    ("reminder-service.openapi.json", "/internal/worker/run", "post"): {"serviceKey"},
    ("price-alert-service.openapi.json", "/internal/worker/run", "post"): {"serviceKey"},
    ("operations-service.openapi.json", "/internal/operations/audit", "post"): {"serviceKey"},
    ("operations-service.openapi.json", "/internal/operations/audit", "get"): {"bearerAuth"},
    ("market-data-service.openapi.json", "/internal/admin/symbols/{raw}", "put"): {"serviceKey"},
    ("market-data-service.openapi.json", "/internal/admin/provider-runs", "post"): {"serviceKey"},
    ("market-data-service.openapi.json", "/internal/admin/provider-runs/{id}/bars", "put"): {"serviceKey"},
    ("market-data-service.openapi.json", "/internal/admin/provider-runs/{id}/complete", "post"): {"serviceKey"},
    ("content-service.openapi.json", "/internal/admin/posts", "post"): {"bearerAuth"},
    ("stock-research-service.openapi.json", "/internal/stocks", "get"): {"bearerAuth"},
    ("journal-service.openapi.json", "/internal/diaries", "post"): {"bearerAuth"},
    ("edge-api.openapi.json", "/api/auth/login", "post"): set(),
    ("edge-api.openapi.json", "/api/auth/api-key/token", "post"): set(),
    ("edge-api.openapi.json", "/api/app/diaries", "get"): {"bearerAuth"},
    ("edge-api.openapi.json", "/api/admin/operations/audit", "get"): {"bearerAuth"},
}
for (filename, endpoint, method), wanted in expected.items():
    actual = security_for(documents[filename], endpoint, method)
    if actual != wanted:
        raise SystemExit(f"security parity failed: {filename} {method.upper()} {endpoint}: expected {sorted(wanted)}, got {sorted(actual)}")

edge_audit = documents["edge-api.openapi.json"]["paths"]["/api/admin/operations/audit"]
if "post" in edge_audit:
    raise SystemExit("security parity failed: Edge must not expose POST /api/admin/operations/audit")

operation_count = 0
for filename, document in documents.items():
    for endpoint, path_item in document["paths"].items():
        for method, operation in path_item.items():
            if method not in {"get", "post", "put", "patch", "delete"}:
                continue
            operation_count += 1
            actual = security_for(document, endpoint, method)
            if actual not in (set(), {"bearerAuth"}, {"serviceKey"}):
                raise SystemExit(f"security parity failed: {filename} {method.upper()} {endpoint} has invalid schemes {sorted(actual)}")

            if filename == "edge-api.openapi.json":
                anonymous = endpoint.startswith("/api/content/") or endpoint in {
                    "/api/auth/register", "/api/auth/login", "/api/auth/refresh",
                    "/api/auth/logout", "/api/auth/api-key/token",
                }
            else:
                anonymous = endpoint.startswith("/health/") or endpoint in {"/openapi.json", "/version"}
                anonymous |= filename == "identity-service.openapi.json" and (
                    endpoint.startswith("/.well-known/") or endpoint in {
                        "/internal/auth/sso/providers", "/internal/auth/register", "/internal/auth/login",
                        "/internal/auth/refresh", "/internal/auth/logout", "/internal/auth/api-key/token",
                    }
                )
                anonymous |= filename == "content-service.openapi.json" and endpoint.startswith("/internal/posts")
                anonymous |= filename == "market-data-service.openapi.json" and endpoint.startswith("/internal/v1/")

            if anonymous and actual:
                raise SystemExit(f"security parity failed: anonymous {filename} {method.upper()} {endpoint} declares {sorted(actual)}")
            if not anonymous and not actual:
                raise SystemExit(f"security parity failed: protected {filename} {method.upper()} {endpoint} declares no security")
            if endpoint.startswith(("/internal/worker/", "/internal/events/")) and actual != {"serviceKey"}:
                raise SystemExit(f"security parity failed: internal worker/event {filename} {method.upper()} {endpoint} is not serviceKey")

print(f"validated authorization security parity for all {operation_count} contract operations")
