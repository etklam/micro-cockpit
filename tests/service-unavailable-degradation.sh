#!/usr/bin/env bash
set -euo pipefail

# Requires the compose stack and seeded local user. It temporarily stops only
# the optional Reminder service and always starts it again.
edge=${EDGE_URL:-http://127.0.0.1:5099}
: "${TEST_EMAIL:?set TEST_EMAIL to a local test account}"
: "${TEST_PASSWORD:?set TEST_PASSWORD}"
command -v jq >/dev/null

login=$(curl -fsS -H 'Content-Type: application/json' \
  -d "$(jq -nc --arg email "$TEST_EMAIL" --arg password "$TEST_PASSWORD" '{email:$email,password:$password}')" \
  "$edge/api/auth/login")
auth="Authorization: Bearer $(jq -er .accessToken <<<"$login")"

docker compose stop reminder >/dev/null
trap 'docker compose start journal reminder >/dev/null' EXIT

dashboard=$(curl -fsS -H "$auth" "$edge/api/app/dashboard")
test "$(jq -r .capabilities.alerts <<<"$dashboard")" = unavailable
calendar=$(curl -fsS -H "$auth" "$edge/api/app/calendar?year=2026&month=7")
test "$(jq -r .capabilities.alerts <<<"$calendar")" = unavailable

# Required Journal failure must be explicit 503, never an empty success.
docker compose stop journal >/dev/null
code=$(curl -sS -o /dev/null -w '%{http_code}' -H "$auth" "$edge/api/app/dashboard")
docker compose start journal >/dev/null
test "$code" = 503

echo "service degradation: optional reminder degrades; required journal returns 503"
