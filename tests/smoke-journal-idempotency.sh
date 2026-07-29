#!/usr/bin/env bash
set -euo pipefail

edge=${EDGE_URL:-http://127.0.0.1:5099}
email=${TEST_EMAIL:-owner@example.com}
password=${TEST_PASSWORD:?set TEST_PASSWORD}
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

access=$(curl -fsS -H 'Content-Type: application/json' \
  -d "$(jq -nc --arg email "$email" --arg password "$password" '{email:$email,password:$password}')" \
  "$edge/api/auth/login" | jq -er .accessToken)
auth="Authorization: Bearer $access"
payload='{"content":"Idempotent market observation"}'

post() {
  curl -fsS -H "$auth" -H 'Content-Type: application/json' \
    -H 'Idempotency-Key: observation-key' -d "$1" "$edge/api/app/quick-observations"
}

post "$payload" >"$tmp/one" &
post "$payload" >"$tmp/two" &
wait
cmp "$tmp/one" "$tmp/two"
test "$(jq -er .observationUpdateId "$tmp/one")" != ""
test "$(curl -sS -o /dev/null -w '%{http_code}' -H "$auth" -H 'Content-Type: application/json' \
  -H 'Idempotency-Key: observation-key' -d '{"content":"Changed payload"}' \
  "$edge/api/app/quick-observations")" = 409

echo 'market observation idempotency smoke: ok'
