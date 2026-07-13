#!/usr/bin/env bash
set -euo pipefail

edge=${EDGE_URL:-http://127.0.0.1:5099}
email=${TEST_EMAIL:-owner@example.com}
password=${TEST_PASSWORD:-correct-horse-battery-staple}
status() { curl -sS -o /dev/null -w '%{http_code}' "$@"; }

login=$(curl -sS -H 'Content-Type: application/json' \
  -d "$(jq -nc --arg email "$email" --arg password "$password" '{email:$email,password:$password}')" "$edge/api/auth/login")
access=$(jq -r .accessToken <<<"$login")

test "$(status -H 'X-User-Id: c7663093-4fc3-4971-bb41-cee9c4bdfa68' "$edge/api/app/diaries")" = 401
test "$(status -H 'Authorization: Bearer invalid' "$edge/api/app/diaries")" = 401
test "$(status -H "Authorization: Bearer $access" "$edge/api/app/diaries")" = 200
test "$(status -H "Authorization: Bearer $access" -H 'Content-Type: application/json' \
  -d '{"localDate":"2026-07-11","content":"JWT-owned quick note"}' "$edge/api/app/quick-note")" = 201

echo 'jwt ownership smoke: ok'
