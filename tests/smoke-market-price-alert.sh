#!/usr/bin/env bash
set -euo pipefail
identity=${IDENTITY_URL:-http://127.0.0.1:5100}
market=${MARKET_DATA_URL:-http://127.0.0.1:5105}
alerts=${PRICE_ALERT_URL:-http://127.0.0.1:5106}
key=${SERVICE_KEY:-local-service-key}
registration_key=${REGISTRATION_KEY:-local-test}
admin="X-Service-Key: $key"
status() { curl -sS -o /dev/null -w '%{http_code}' "$@"; }

access=$(curl -sS -H 'Content-Type: application/json' -d '{"email":"owner@example.com","password":"correct-horse-battery-staple"}' "$identity/internal/auth/login" | jq -r .accessToken)
owner="Authorization: Bearer $access"
other_email="market-other-$(date +%s)@example.com"
curl -sS -o /dev/null -H "X-Registration-Key: $registration_key" -H 'Content-Type: application/json' -d "{\"email\":\"$other_email\",\"password\":\"correct-horse-battery-staple\",\"displayName\":\"Market Other\",\"timezone\":\"UTC\",\"baseCurrency\":\"USD\"}" "$identity/internal/auth/register"
other_access=$(curl -sS -H 'Content-Type: application/json' -d "{\"email\":\"$other_email\",\"password\":\"correct-horse-battery-staple\"}" "$identity/internal/auth/login" | jq -r .accessToken)
other="Authorization: Bearer $other_access"

curl -sS -o /dev/null -H "$admin" -H 'Content-Type: application/json' -X PUT -d '{"name":"Apple Inc.","exchange":"NASDAQ","currency":"USD","timezone":"America/New_York"}' "$market/internal/admin/symbols/AAPL"
run=$(curl -sS -H "$admin" -H 'Content-Type: application/json' -d '{"provider":"smoke"}' "$market/internal/admin/provider-runs" | jq -r .id)
curl -sS -o /dev/null -H "$admin" -H 'Content-Type: application/json' -X PUT -d '[{"symbol":"AAPL","tradingDate":"2026-07-09","open":95,"high":101,"low":94,"close":100,"volume":1000},{"symbol":"AAPL","tradingDate":"2026-07-10","open":100,"high":111,"low":99,"close":110,"volume":1200}]' "$market/internal/admin/provider-runs/$run/bars"
test "$(curl -sS "$market/internal/v1/bars/AAPL?from=2026-07-01&to=2026-07-31" | jq '.items|length')" = 0
test "$(status -H "$owner" -H 'Content-Type: application/json' -d '{"symbol":"AAPL","conditionType":"above","threshold":105}' "$alerts/internal/price-alerts")" = 503
curl -sS -o /dev/null -H "$admin" -H 'Content-Type: application/json' -d '{"status":"succeeded"}' "$market/internal/admin/provider-runs/$run/complete"
test "$(curl -sS "$market/internal/v1/bars/AAPL?from=2026-07-01&to=2026-07-31" | jq '.items|length')" = 2
test "$(docker exec micro-cockpit-postgres-1 psql -U trade_diary -d trade_diary -Atc "select count(*) from market_data_public.adjusted_daily_bars_v1 where symbol='AAPL'")" = 2

alert=$(curl -sS -H "$owner" -H 'Content-Type: application/json' -d '{"symbol":"AAPL","conditionType":"above","threshold":105}' "$alerts/internal/price-alerts")
id=$(jq -r .id <<<"$alert")
# Other condition contracts are accepted and active.
curl -sS -o /dev/null -H "$owner" -H 'Content-Type: application/json' -d '{"symbol":"AAPL","conditionType":"below","threshold":50}' "$alerts/internal/price-alerts"
curl -sS -o /dev/null -H "$owner" -H 'Content-Type: application/json' -d '{"symbol":"AAPL","conditionType":"percent_change","threshold":5}' "$alerts/internal/price-alerts"
curl -sS -o /dev/null -H "$owner" -H 'Content-Type: application/json' -d '{"symbol":"AAPL","conditionType":"ma_crossing","threshold":0,"lookbackDays":2,"direction":"above"}' "$alerts/internal/price-alerts"
test "$(status -H "$other" -X DELETE "$alerts/internal/price-alerts/$id")" = 404
test "$(curl -sS -H "$owner" -X POST "$alerts/internal/worker/run" | jq -r .triggered)" -ge 1
test "$(curl -sS -H "$owner" -X POST "$alerts/internal/worker/run" | jq -r .triggered)" = 0
test "$(curl -sS -H "$owner" "$alerts/internal/price-alerts" | jq -r ".items[]|select(.id==\"$id\")|.status")" = triggered
curl -sS -o /dev/null -H "$owner" -X POST "$alerts/internal/price-alerts/$id/dismiss"
curl -sS -o /dev/null -H "$owner" -X POST "$alerts/internal/price-alerts/$id/reactivate"
test "$(curl -sS -H "$owner" "$alerts/internal/price-alerts" | jq -r ".items[]|select(.id==\"$id\")|.status")" = active
echo 'market + price alert smoke: ok'
