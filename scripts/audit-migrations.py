#!/usr/bin/env python3
"""Static migration inventory and schema/role ownership audit (stdlib only)."""

import json
import re
import sys
from pathlib import Path

root = Path(__file__).resolve().parents[1]
ownership = json.loads((root / "contracts/schema-ownership.json").read_text())["services"]
sql_files = sorted((root / "platform/postgres/init").glob("*.sql"))
roles_sql = (root / "platform/postgres/roles/001_service_roles.sql").read_text()
all_sql = "\n".join(p.read_text() for p in sql_files)
created = set(re.findall(r"CREATE\s+SCHEMA\s+(?:IF\s+NOT\s+EXISTS\s+)?([a-z_][a-z0-9_]*)", all_sql, re.I))
tables = [(p.name, schema, table) for p in sql_files for schema, table in re.findall(r"CREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?([a-z_][a-z0-9_]*)\.([a-z_][a-z0-9_]*)", p.read_text(), re.I)]
errors = []

for service, policy in ownership.items():
    role = service.replace("-service", "_service").replace("-", "_")
    for schema in policy["owns"]:
        if schema not in created:
            errors.append(f"{service}: schema {schema} is not created")
        if not re.search(rf"GRANT\s+USAGE(?:,\s*CREATE)?\s+ON\s+SCHEMA[^;]*\b{re.escape(schema)}\b[^;]*\b{re.escape(role)}\b", roles_sql, re.I):
            errors.append(f"{service}: role {role} lacks explicit schema grant for {schema}")
    for schema in policy["reads"]:
        if not re.search(rf"GRANT\s+USAGE\s+ON\s+SCHEMA\s+{re.escape(schema)}\s+TO[^;]*\b{re.escape(role)}\b", roles_sql, re.I):
            errors.append(f"{service}: read schema {schema} lacks USAGE grant")

owned = {schema for policy in ownership.values() for schema in policy["owns"]}
for filename, schema, table in tables:
    if schema not in owned:
        errors.append(f"{filename}: {schema}.{table} has no catalog owner")

if errors:
    print("migration ownership audit failed:", *errors, sep="\n- ")
    sys.exit(1)
print(f"migration ownership verified: {len(sql_files)} files, {len(created)} schemas, {len(tables)} tables")
for filename, schema, table in tables:
    print(f"{filename}\t{schema}.{table}")
