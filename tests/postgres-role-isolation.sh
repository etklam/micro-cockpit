#!/bin/sh
set -eu

for name in IDENTITY_DB_PASSWORD JOURNAL_DB_PASSWORD PRICE_ALERT_DB_PASSWORD ROTATION_DB_PASSWORD; do
  eval "value=\${$name:-}"
  [ -n "$value" ] || { echo "$name is required" >&2; exit 1; }
done

sql() {
  role=$1 password=$2 statement=$3
  docker compose exec -T -e PGPASSWORD="$password" postgres \
    psql -v ON_ERROR_STOP=1 -U "$role" -d trade_diary -c "$statement" >/dev/null
}

must_deny() {
  role=$1 password=$2 statement=$3
  if sql "$role" "$password" "$statement" 2>/dev/null; then
    echo "isolation failure: $role executed: $statement" >&2
    exit 1
  fi
}

# Own-schema DML works (zero-row updates avoid changing application data).
sql journal_service "$JOURNAL_DB_PASSWORD" 'UPDATE journal.diaries SET updated_at=updated_at WHERE false'
sql identity_service "$IDENTITY_DB_PASSWORD" 'UPDATE identity.users SET display_name=display_name WHERE false'

# Runtime services cannot write another service's schema.
must_deny journal_service "$JOURNAL_DB_PASSWORD" 'DELETE FROM identity.users WHERE false'
must_deny identity_service "$IDENTITY_DB_PASSWORD" 'DELETE FROM journal.diaries WHERE false'

# Market consumers can read only published views, never market base tables.
sql price_alert_service "$PRICE_ALERT_DB_PASSWORD" 'SELECT 1 FROM market_data_public.daily_bar_prices_v1 LIMIT 0'
sql price_alert_service "$PRICE_ALERT_DB_PASSWORD" 'SELECT 1 FROM market.published_provider_health_v1 LIMIT 0'
sql rotation_service "$ROTATION_DB_PASSWORD" 'SELECT 1 FROM market_data_public.adjusted_daily_bars_v1 LIMIT 0'
must_deny price_alert_service "$PRICE_ALERT_DB_PASSWORD" 'DELETE FROM market.daily_bars WHERE false'
must_deny rotation_service "$ROTATION_DB_PASSWORD" 'DELETE FROM market.daily_bars WHERE false'
must_deny price_alert_service "$PRICE_ALERT_DB_PASSWORD" 'SELECT 1 FROM market_data_public.adjusted_daily_bars_v1 LIMIT 0'
must_deny price_alert_service "$PRICE_ALERT_DB_PASSWORD" 'DELETE FROM market_data_public.daily_bar_prices_v1 WHERE false'
must_deny rotation_service "$ROTATION_DB_PASSWORD" 'DELETE FROM market_data_public.adjusted_daily_bars_v1 WHERE false'

echo 'postgres role isolation: ok'
