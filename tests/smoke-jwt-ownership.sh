#!/usr/bin/env bash
set -euo pipefail

edge=${EDGE_URL:-http://127.0.0.1:5099}
email=${TEST_EMAIL:-owner@example.com}
password=${TEST_PASSWORD:?set TEST_PASSWORD}
status() { curl -sS -o /dev/null -w '%{http_code}' "$@"; }

access=$(curl -fsS -H 'Content-Type: application/json' \
  -d "$(jq -nc --arg email "$email" --arg password "$password" '{email:$email,password:$password}')" \
  "$edge/api/auth/login" | jq -er .accessToken)

test "$(status -H 'X-User-Id: c7663093-4fc3-4971-bb41-cee9c4bdfa68' "$edge/api/app/market-observations/today")" = 401
test "$(status -H 'Authorization: Bearer invalid' "$edge/api/app/market-observations/today")" = 401
test "$(status -H "Authorization: Bearer $access" "$edge/api/app/market-observations/today")" = 200
test "$(status -H "Authorization: Bearer $access" -H 'Content-Type: application/json' \
  -H 'Idempotency-Key: jwt-owned-observation' -d '{"content":"JWT-owned observation"}' \
  "$edge/api/app/quick-observations")" = 200

echo 'jwt ownership smoke: ok'
