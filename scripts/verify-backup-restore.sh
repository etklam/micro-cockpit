#!/usr/bin/env bash
set -euo pipefail

: "${DATABASE_URL:?set DATABASE_URL to the source database}"
command -v pg_dump >/dev/null
command -v pg_restore >/dev/null

dump="${TMPDIR:-/tmp}/micro-cockpit-backup-$$.dump"
trap 'rm -f "$dump"' EXIT
pg_dump --format=custom --no-owner --no-acl --file="$dump" "$DATABASE_URL"
pg_restore --list "$dump" >/dev/null

if [[ -n "${RESTORE_DATABASE_URL:-}" ]]; then
  pg_restore --exit-on-error --no-owner --no-acl --dbname="$RESTORE_DATABASE_URL" "$dump"
  psql "$RESTORE_DATABASE_URL" -v ON_ERROR_STOP=1 -Atc "SELECT count(*) > 0 FROM information_schema.tables WHERE table_schema NOT IN ('pg_catalog','information_schema')" | grep -qx t
fi

echo "backup archive verified${RESTORE_DATABASE_URL:+ and restored to disposable target}"
