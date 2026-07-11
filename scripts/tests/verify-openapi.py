#!/usr/bin/env python3
"""Generate and verify the committed OpenAPI 3.1 contracts (stdlib only)."""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "contracts" / "openapi"
SOURCES = {
    "edge-api": ROOT / "gateway/TradeDiary.EdgeApi/Program.cs",
    **{
        name: ROOT / f"services/{name}/src/TradeDiary.{project}/Program.cs"
        for name, project in {
            "identity-service": "Identity", "journal-service": "Journal",
            "performance-service": "Performance", "discipline-service": "Discipline",
            "reminder-service": "Reminder", "stock-research-service": "StockResearch",
            "market-data-service": "MarketData", "price-alert-service": "PriceAlert",
            "rotation-service": "Rotation", "partner-service": "Partner",
            "content-service": "Content", "tool-service": "Tool",
            "operations-service": "Operations",
        }.items()
    },
}
HTTP = {"Get": "get", "Post": "post", "Put": "put", "Patch": "patch", "Delete": "delete"}
CONSTRAINT = re.compile(r"\{([^}:]+):[^}]+\}")


def normalize(path: str) -> str:
    return CONSTRAINT.sub(r"{\1}", path)


def routes(source: Path) -> set[tuple[str, str]]:
    text = source.read_text()
    found = {
        (normalize(path), HTTP[verb])
        for verb, path in re.findall(r'app\.Map(Get|Post|Put|Patch|Delete)\("([^"]+)"', text)
    }
    # Edge's public proxy table is intentionally data-driven.
    for path, methods in re.findall(r'MapProxy\(app,\s*"([^"]+)".*?\[([^\]]+)\]\)', text):
        found.update((normalize(path), method.lower()) for method in re.findall(r'HttpMethods\.(Get|Post|Put|Patch|Delete)', methods))
    # The four anonymous auth routes are registered in a foreach interpolation.
    if source.name == "Program.cs" and "TradeDiary.EdgeApi" in str(source):
        found.update((f"/api/auth/{action}", "post") for action in ("register", "login", "refresh", "logout"))
    return found


def security(path: str) -> list[dict[str, list[str]]]:
    if path in {"/health/live", "/health/ready", "/version", "/.well-known/openid-configuration", "/.well-known/jwks.json"}:
        return []
    if path.startswith("/api/auth/") or path.startswith("/api/content/") or path in {"/internal/auth/register", "/internal/auth/login", "/internal/auth/refresh", "/internal/auth/logout", "/internal/auth/api-key/token", "/internal/auth/sso/providers", "/internal/posts", "/internal/posts/{slug}"}:
        return []
    if any(part in path for part in ("/internal/admin/", "/internal/worker/", "/internal/events/")):
        return [{"serviceKey": []}]
    return [{"bearerAuth": []}]


def schema_for(path: str) -> tuple[str, str]:
    if path.startswith("/health/"):
        return "Health", "WriteRequest"
    if path == "/version":
        return "Version", "WriteRequest"
    for needle, pair in (
        ("transactions", ("Transaction", "TransactionWrite")),
        ("diaries", ("Diary", "DiaryWrite")),
        ("quick-note", ("Diary", "QuickNoteWrite")),
        ("daily-performance", ("DailyPerformance", "DailyPerformanceWrite")),
        ("daily-performances", ("DailyPerformance", "DailyPerformanceWrite")),
        ("disciplines", ("Discipline", "DisciplineWrite")),
        ("diary-alerts", ("DiaryAlert", "DiaryAlertWrite")),
        ("price-alerts", ("PriceAlert", "PriceAlertWrite")),
        ("watchlist", ("Stock", "StockWrite")),
        ("stocks", ("Stock", "StockWrite")),
        ("rotation/universes", ("RotationUniverse", "RotationUniverseWrite")),
        ("partners", ("Partner", "PartnerWrite")),
        ("posts", ("Post", "PostWrite")),
        ("operations/audit", ("AuditEvent", "AuditWrite")),
        ("market/bars", ("MarketBar", "MarketBarWrite")),
        ("/internal/v1/bars", ("MarketBar", "MarketBarWrite")),
        ("/auth/", ("AuthTokens", "AuthWrite")),
    ):
        if needle in path:
            return pair
    return "JsonValue", "WriteRequest"


def operation(path: str, method: str) -> dict:
    response_schema, write_schema = schema_for(path)
    response = {"$ref": f"#/components/schemas/{response_schema}"}
    if method == "get" and not re.search(r"/\{[^}]+\}$", path):
        response = {"type": "object", "properties": {"items": {"type": "array", "items": response}}}
    op: dict = {
        "operationId": re.sub(r"[^a-zA-Z0-9]+", "_", f"{method}_{path}").strip("_"),
        "responses": {
            "200": {"description": "Success", "content": {"application/json": {"schema": response}}},
            "400": {"$ref": "#/components/responses/Problem"},
            "401": {"$ref": "#/components/responses/Problem"},
            "404": {"$ref": "#/components/responses/Problem"},
        },
        "security": security(path),
    }
    params = re.findall(r"\{([^}]+)\}", path)
    if params:
        op["parameters"] = [{"name": name, "in": "path", "required": True, "schema": {"type": "string"}} for name in params]
    if method == "post" and (path in {"/internal/diaries", "/internal/quick-note"} or path == "/internal/diaries/{diaryId}/transactions"):
        op.setdefault("parameters", []).append({"name": "Idempotency-Key", "in": "header", "required": False, "schema": {"type": "string", "maxLength": 200}})
    if method in {"post", "put", "patch"}:
        op["requestBody"] = {"required": True, "content": {"application/json": {"schema": {"$ref": f"#/components/schemas/{write_schema}"}}}}
    if method == "delete":
        op["responses"]["204"] = {"description": "Deleted"}
    return op


def document(name: str, source: Path) -> dict:
    paths: dict[str, dict] = {}
    for path, method in sorted(routes(source)):
        paths.setdefault(path, {})[method] = operation(path, method)
    return {
        "openapi": "3.1.0",
        "info": {"title": f"Trade Diary {name}", "version": "0.1.0"},
        "jsonSchemaDialect": "https://json-schema.org/draft/2020-12/schema",
        "paths": paths,
        "components": {
            "securitySchemes": {
                "bearerAuth": {"type": "http", "scheme": "bearer", "bearerFormat": "JWT"},
                "serviceKey": {"type": "apiKey", "in": "header", "name": "X-Service-Key"},
            },
            "schemas": {
                "JsonValue": {},
                "WriteRequest": {"type": "object", "additionalProperties": True},
                "Health": {"type": "object", "required": ["status"], "properties": {"status": {"type": "string"}}},
                "Version": {"type": "object", "required": ["service", "version"], "properties": {"service": {"type": "string"}, "version": {"type": "string"}}},
                "Problem": {"type": "object", "properties": {"error": {"type": "string"}, "detail": {"type": "string"}}},
                "Collection": {"type": "object", "required": ["items"], "properties": {"items": {"type": "array", "items": {"$ref": "#/components/schemas/JsonValue"}}}},
                "AuthTokens": {"type": "object", "required": ["accessToken", "refreshToken"], "properties": {"accessToken": {"type": "string"}, "refreshToken": {"type": "string"}, "expiresIn": {"type": "integer"}}},
                "AuthWrite": {"type": "object", "properties": {"email": {"type": "string", "format": "email"}, "password": {"type": "string"}, "refreshToken": {"type": "string"}}},
                "Diary": {"type": "object", "properties": {"id": {"type": "string", "format": "uuid"}, "title": {"type": "string"}, "content": {"type": "string"}, "localDate": {"type": "string", "format": "date"}}},
                "DiaryWrite": {"type": "object", "required": ["title", "content", "localDate"], "properties": {"title": {"type": "string"}, "content": {"type": "string"}, "localDate": {"type": "string", "format": "date"}}},
                "QuickNoteWrite": {"type": "object", "required": ["content"], "properties": {"content": {"type": "string"}, "diaryId": {"type": ["string", "null"], "format": "uuid"}}},
                "Transaction": {"type": "object", "properties": {"id": {"type": "string", "format": "uuid"}, "symbol": {"type": "string"}, "side": {"type": "string"}, "quantity": {"type": "number"}, "price": {"type": "number"}}},
                "TransactionWrite": {"type": "object", "required": ["symbol", "side", "quantity", "price"], "properties": {"symbol": {"type": "string"}, "side": {"type": "string"}, "quantity": {"type": "number", "exclusiveMinimum": 0}, "price": {"type": "number", "minimum": 0}}},
                "DailyPerformance": {"type": "object", "properties": {"localDate": {"type": "string", "format": "date"}, "profitLoss": {"type": "number"}, "percentReturn": {"type": ["number", "null"]}}},
                "DailyPerformanceWrite": {"type": "object", "required": ["profitLoss"], "properties": {"profitLoss": {"type": "number"}, "capitalBase": {"type": ["number", "null"], "exclusiveMinimum": 0}}},
                "Discipline": {"type": "object", "properties": {"id": {"type": "string", "format": "uuid"}, "text": {"type": "string"}, "position": {"type": "integer"}}},
                "DisciplineWrite": {"type": "object", "required": ["text"], "properties": {"text": {"type": "string"}, "ids": {"type": "array", "items": {"type": "string", "format": "uuid"}}}},
                "DiaryAlert": {"type": "object", "properties": {"id": {"type": "string", "format": "uuid"}, "diaryId": {"type": "string", "format": "uuid"}, "scheduledFor": {"type": "string", "format": "date-time"}, "status": {"type": "string"}}},
                "DiaryAlertWrite": {"type": "object", "required": ["diaryId", "scheduledFor"], "properties": {"diaryId": {"type": "string", "format": "uuid"}, "scheduledFor": {"type": "string", "format": "date-time"}, "recurrence": {"type": "string"}}},
                "Stock": {"type": "object", "properties": {"id": {"type": "string", "format": "uuid"}, "symbol": {"type": "string"}, "name": {"type": "string"}}},
                "StockWrite": {"type": "object", "required": ["symbol"], "properties": {"symbol": {"type": "string"}, "name": {"type": "string"}}},
                "PriceAlert": {"type": "object", "properties": {"id": {"type": "string", "format": "uuid"}, "symbol": {"type": "string"}, "threshold": {"type": "number"}, "status": {"type": "string"}}},
                "PriceAlertWrite": {"type": "object", "required": ["symbol", "conditionType", "threshold"], "properties": {"symbol": {"type": "string"}, "conditionType": {"type": "string"}, "threshold": {"type": "number"}}},
                "RotationUniverse": {"type": "object", "properties": {"id": {"type": "string", "format": "uuid"}, "name": {"type": "string"}, "symbols": {"type": "array", "items": {"type": "string"}}}},
                "RotationUniverseWrite": {"type": "object", "required": ["name"], "properties": {"name": {"type": "string"}, "symbols": {"type": "array", "items": {"type": "string"}}}},
                "Partner": {"type": "object", "properties": {"id": {"type": "string", "format": "uuid"}, "status": {"type": "string"}, "partnerUserId": {"type": "string", "format": "uuid"}}},
                "PartnerWrite": {"type": "object", "properties": {"email": {"type": "string", "format": "email"}, "resources": {"type": "array", "items": {"type": "string"}}}},
                "Post": {"type": "object", "properties": {"id": {"type": "string", "format": "uuid"}, "slug": {"type": "string"}, "title": {"type": "string"}, "body": {"type": "string"}}},
                "PostWrite": {"type": "object", "required": ["slug", "title"], "properties": {"slug": {"type": "string"}, "title": {"type": "string"}, "body": {"type": "string"}, "status": {"type": "string"}}},
                "AuditEvent": {"type": "object", "properties": {"id": {"type": "string", "format": "uuid"}, "action": {"type": "string"}, "resourceType": {"type": "string"}, "occurredAt": {"type": "string", "format": "date-time"}}},
                "AuditWrite": {"type": "object", "required": ["action", "resourceType"], "properties": {"action": {"type": "string"}, "resourceType": {"type": "string"}, "resourceId": {"type": ["string", "null"]}}},
                "MarketBar": {"type": "object", "properties": {"tradingDate": {"type": "string", "format": "date"}, "open": {"type": "number"}, "high": {"type": "number"}, "low": {"type": "number"}, "close": {"type": "number"}, "volume": {"type": "number"}}},
                "MarketBarWrite": {"type": "object", "required": ["tradingDate", "close"], "properties": {"tradingDate": {"type": "string", "format": "date"}, "close": {"type": "number"}}},
            },
            "responses": {"Problem": {"description": "Request failed", "content": {"application/problem+json": {"schema": {"$ref": "#/components/schemas/Problem"}}}}},
        },
    }


def verify(name: str, source: Path) -> list[str]:
    file = OUT / f"{name}.openapi.json"
    if not file.exists():
        return [f"{file.relative_to(ROOT)}: missing"]
    try:
        doc = json.loads(file.read_text())
    except (json.JSONDecodeError, OSError) as error:
        return [f"{file.relative_to(ROOT)}: invalid JSON: {error}"]
    errors = []
    if doc.get("openapi") != "3.1.0" or not isinstance(doc.get("info"), dict) or not isinstance(doc.get("paths"), dict):
        errors.append(f"{file.relative_to(ROOT)}: invalid OpenAPI 3.1 structure")
        return errors
    schemes = doc.get("components", {}).get("securitySchemes", {})
    if not {"bearerAuth", "serviceKey"} <= schemes.keys():
        errors.append(f"{file.relative_to(ROOT)}: bearerAuth/serviceKey missing")
    actual = routes(source)
    contracted = {(path, method) for path, item in doc["paths"].items() for method in item if method in HTTP.values()}
    for route in sorted(actual - contracted):
        errors.append(f"{file.relative_to(ROOT)}: route missing: {route[1].upper()} {route[0]}")
    for route in sorted(contracted - actual):
        errors.append(f"{file.relative_to(ROOT)}: stale route: {route[1].upper()} {route[0]}")
    for required in (("/health/live", "get"), ("/health/ready", "get"), ("/version", "get")):
        if required not in contracted:
            errors.append(f"{file.relative_to(ROOT)}: required operational route missing: {required[1].upper()} {required[0]}")
    for path, method in contracted:
        op = doc["paths"][path][method]
        if "responses" not in op or "security" not in op:
            errors.append(f"{file.relative_to(ROOT)}: incomplete operation: {method.upper()} {path}")
    encoded = json.dumps(doc)
    for ref in re.findall(r'"\$ref":\s*"#/components/schemas/([^"]+)"', encoded):
        if ref not in doc.get("components", {}).get("schemas", {}):
            errors.append(f"{file.relative_to(ROOT)}: unresolved schema reference: {ref}")
    return errors


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--write", action="store_true", help="regenerate contracts from Program.cs")
    args = parser.parse_args()
    if args.write:
        OUT.mkdir(parents=True, exist_ok=True)
        for name, source in SOURCES.items():
            (OUT / f"{name}.openapi.json").write_text(json.dumps(document(name, source), indent=2) + "\n")
    errors = [error for name, source in SOURCES.items() for error in verify(name, source)]
    if errors:
        print("\n".join(errors), file=sys.stderr)
        return 1
    count = sum(len(routes(source)) for source in SOURCES.values())
    print(f"OpenAPI contracts verified: {len(SOURCES)} documents, {count} operations")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
