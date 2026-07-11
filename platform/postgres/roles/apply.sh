#!/bin/sh
set -eu

required='MIGRATOR_DB_PASSWORD IDENTITY_DB_PASSWORD JOURNAL_DB_PASSWORD PERFORMANCE_DB_PASSWORD DISCIPLINE_DB_PASSWORD REMINDER_DB_PASSWORD MARKET_DATA_DB_PASSWORD PRICE_ALERT_DB_PASSWORD ROTATION_DB_PASSWORD STOCK_RESEARCH_DB_PASSWORD PARTNER_DB_PASSWORD CONTENT_DB_PASSWORD OPERATIONS_DB_PASSWORD'
for name in $required; do
  eval "value=\${$name:-}"
  [ -n "$value" ] || { echo "$name is required" >&2; exit 1; }
done

psql -v ON_ERROR_STOP=1 -f /roles/001_service_roles.sql
psql -v ON_ERROR_STOP=1 \
  -v migrator_password="$MIGRATOR_DB_PASSWORD" \
  -v identity_password="$IDENTITY_DB_PASSWORD" \
  -v journal_password="$JOURNAL_DB_PASSWORD" \
  -v performance_password="$PERFORMANCE_DB_PASSWORD" \
  -v discipline_password="$DISCIPLINE_DB_PASSWORD" \
  -v reminder_password="$REMINDER_DB_PASSWORD" \
  -v market_data_password="$MARKET_DATA_DB_PASSWORD" \
  -v price_alert_password="$PRICE_ALERT_DB_PASSWORD" \
  -v rotation_password="$ROTATION_DB_PASSWORD" \
  -v stock_research_password="$STOCK_RESEARCH_DB_PASSWORD" \
  -v partner_password="$PARTNER_DB_PASSWORD" \
  -v content_password="$CONTENT_DB_PASSWORD" \
  -v operations_password="$OPERATIONS_DB_PASSWORD" \
  -f /roles/002_passwords.sql
