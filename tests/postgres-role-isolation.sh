#!/bin/sh
set -eu

for name in IDENTITY_DB_PASSWORD JOURNAL_DB_PASSWORD MARKET_DATA_DB_PASSWORD TOOL_DB_PASSWORD; do
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
sql journal_service "$JOURNAL_DB_PASSWORD" 'UPDATE journal.market_observations SET updated_at=updated_at WHERE false'
sql identity_service "$IDENTITY_DB_PASSWORD" 'UPDATE identity.users SET display_name=display_name WHERE false'
sql market_data_service "$MARKET_DATA_DB_PASSWORD" 'UPDATE market.instruments SET created_at=created_at WHERE false'
sql tool_service "$TOOL_DB_PASSWORD" 'UPDATE tool.presets SET updated_at=updated_at WHERE false'

# Runtime services cannot write another service's schema.
must_deny journal_service "$JOURNAL_DB_PASSWORD" 'DELETE FROM identity.users WHERE false'
must_deny identity_service "$IDENTITY_DB_PASSWORD" 'DELETE FROM journal.market_observations WHERE false'
must_deny tool_service "$TOOL_DB_PASSWORD" 'DELETE FROM journal.market_observations WHERE false'
must_deny journal_service "$JOURNAL_DB_PASSWORD" 'DELETE FROM tool.presets WHERE false'

# Market Data owns base tables and its public evidence views; other services do not.
sql market_data_service "$MARKET_DATA_DB_PASSWORD" 'SELECT 1 FROM market_data_public.daily_bar_prices_v1 LIMIT 0'
sql market_data_service "$MARKET_DATA_DB_PASSWORD" 'SELECT 1 FROM market_data_public.adjusted_daily_bars_v1 LIMIT 0'
must_deny journal_service "$JOURNAL_DB_PASSWORD" 'SELECT 1 FROM market.daily_bars LIMIT 0'
must_deny tool_service "$TOOL_DB_PASSWORD" 'DELETE FROM market_data_public.daily_bar_prices_v1 WHERE false'

echo 'postgres role isolation: ok'
