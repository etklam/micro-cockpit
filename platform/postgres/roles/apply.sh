#!/bin/sh
set -eu

mode=${1:-}
case "$mode" in bootstrap|finalize) ;; *) echo "Usage: $0 bootstrap|finalize" >&2; exit 2 ;; esac

if [ "$mode" = finalize ]; then
  exec psql -v ON_ERROR_STOP=1 -f /roles/003_finalize_grants.sql
fi

required='MIGRATOR_DB_PASSWORD IDENTITY_DB_PASSWORD JOURNAL_DB_PASSWORD PERFORMANCE_DB_PASSWORD DISCIPLINE_DB_PASSWORD REMINDER_DB_PASSWORD MARKET_DATA_DB_PASSWORD PRICE_ALERT_DB_PASSWORD ROTATION_DB_PASSWORD STOCK_RESEARCH_DB_PASSWORD PARTNER_DB_PASSWORD CONTENT_DB_PASSWORD TOOL_DB_PASSWORD OPERATIONS_DB_PASSWORD'
for name in $required; do
  value=$(printenv "$name" || true)
  [ -n "$value" ] || { echo "$name is required" >&2; exit 1; }
done

psql -v ON_ERROR_STOP=1 -f /roles/001_bootstrap_roles.sql
{
  printf '%s\n' 'BEGIN;' 'CREATE TEMP TABLE role_credentials(role_name text PRIMARY KEY, encoded_password text NOT NULL);' 'COPY role_credentials(role_name, encoded_password) FROM STDIN;'
  for mapping in \
    trade_diary_migrator:MIGRATOR_DB_PASSWORD identity_service:IDENTITY_DB_PASSWORD \
    journal_service:JOURNAL_DB_PASSWORD performance_service:PERFORMANCE_DB_PASSWORD \
    discipline_service:DISCIPLINE_DB_PASSWORD reminder_service:REMINDER_DB_PASSWORD \
    market_data_service:MARKET_DATA_DB_PASSWORD price_alert_service:PRICE_ALERT_DB_PASSWORD \
    rotation_service:ROTATION_DB_PASSWORD stock_research_service:STOCK_RESEARCH_DB_PASSWORD \
    partner_service:PARTNER_DB_PASSWORD content_service:CONTENT_DB_PASSWORD tool_service:TOOL_DB_PASSWORD operations_service:OPERATIONS_DB_PASSWORD
  do
    role=${mapping%%:*}
    key=${mapping#*:}
    value=$(printenv "$key")
    encoded=$(printf '%s' "$value" | base64 | tr -d '\n')
    printf '%s\t%s\n' "$role" "$encoded"
  done
  printf '%s\n' '\.'
  cat /roles/002_role_passwords.sql
  printf '%s\n' 'COMMIT;'
} | psql -v ON_ERROR_STOP=1 >/dev/null
