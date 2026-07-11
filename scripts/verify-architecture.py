#!/usr/bin/env python3
import json, re, sys
from pathlib import Path

root = Path(__file__).resolve().parents[1]
errors = []
ownership = json.loads((root / "contracts/schema-ownership.json").read_text())["services"]

for service, rules in ownership.items():
    base = root / "services" / service
    if not (base / "SERVICE.md").is_file(): errors.append(f"{service}: missing SERVICE.md")
    dockerfiles = list(base.rglob("Dockerfile"))
    if not dockerfiles: errors.append(f"{service}: missing Dockerfile")
    source = "\n".join(p.read_text(errors="ignore") for p in base.rglob("*.cs"))
    for endpoint in ("/health/live", "/health/ready", "/version"):
        if endpoint not in source: errors.append(f"{service}: missing {endpoint}")
    for schema in rules["owns"]:
        migrations = "\n".join(p.read_text(errors="ignore") for p in (root / "platform/postgres/init").glob("*.sql"))
        if not re.search(rf"CREATE\s+SCHEMA\s+(?:IF\s+NOT\s+EXISTS\s+)?{re.escape(schema)}\b", migrations, re.I):
            errors.append(f"{service}: no migration creates schema {schema}")
    allowed = set(rules["owns"])
    for match in re.finditer(r"\b(?:INSERT\s+INTO|UPDATE|DELETE\s+FROM)\s+([a-z_][a-z0-9_]*)\.", source, re.I):
        if match.group(1).lower() not in allowed:
            errors.append(f"{service}: cross-schema DML to {match.group(1)}")

frontend = "\n".join(p.read_text(errors="ignore") for p in (root / "frontend/src").rglob("*.*") if p.is_file())
if re.search(r"https?://(?:localhost|127\.0\.0\.1|[a-z0-9-]+-service)(?::\d+)?", frontend, re.I):
    errors.append("frontend: contains an internal service URL")

for schema_file in (root / "contracts/events").glob("*.schema.json"):
    try:
        schema = json.loads(schema_file.read_text())
        if schema.get("$schema") != "https://json-schema.org/draft/2020-12/schema" or schema.get("type") != "object" or not schema.get("required"):
            errors.append(f"{schema_file.name}: incomplete JSON Schema contract")
    except json.JSONDecodeError as exc:
        errors.append(f"{schema_file.name}: invalid JSON: {exc}")

if errors:
    print("architecture verification failed:", *errors, sep="\n- ")
    sys.exit(1)
print(f"architecture verified: {len(ownership)} services, {len(list((root / 'contracts/events').glob('*.schema.json')))} event schemas")
