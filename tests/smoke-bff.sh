#!/usr/bin/env bash
set -euo pipefail
: "${TEST_PASSWORD:?set TEST_PASSWORD}"
edge=http://127.0.0.1:5099
status() { curl -sS -o /dev/null -w '%{http_code}' "$@"; }

test "$(status "$edge/api/app/dashboard")" = 401
login=$(curl -sS -H 'Content-Type: application/json' -d "{\"email\":\"owner@example.com\",\"password\":\"${TEST_PASSWORD}\"}" "$edge/api/auth/login")
access=$(jq -r .accessToken <<<"$login"); auth="Authorization: Bearer $access"
test "$(status -H "$auth" -H 'Content-Type: application/json' -d '{"localDate":"2026-07-11","content":"Saved through Edge API"}' "$edge/api/app/quick-note")" = 201

dashboard=$(curl -sS -H "$auth" -H 'X-Correlation-ID: bff-smoke-123' "$edge/api/app/dashboard")
test "$(jq -r .localDate <<<"$dashboard")" = '2026-07-11'
test "$(jq -r '.diary.writtenToday' <<<"$dashboard")" = true
test "$(jq -r '.performance == null' <<<"$dashboard")" = true
test "$(jq -r '.capabilities.alerts' <<<"$dashboard")" = available
correlation=$(curl -sS -o /dev/null -D - -H "$auth" -H 'X-Correlation-ID: bff-smoke-123' "$edge/api/app/dashboard" | awk 'tolower($1)=="x-correlation-id:" {gsub("\r", "", $2); print $2}')
test "$correlation" = bff-smoke-123

calendar=$(curl -sS -H "$auth" "$edge/api/app/calendar?year=2026&month=7")
test "$(jq '.days | length' <<<"$calendar")" = 31
test "$(jq -r '.days[] | select(.date=="2026-07-11") | .performance == null' <<<"$calendar")" = true

echo 'bff smoke: ok'
