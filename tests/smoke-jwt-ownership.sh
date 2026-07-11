#!/usr/bin/env bash
set -euo pipefail

identity=http://127.0.0.1:5100
journal=http://127.0.0.1:5101
status() { curl -sS -o /dev/null -w '%{http_code}' "$@"; }

login=$(curl -sS -H 'Content-Type: application/json' \
  -d '{"email":"owner@example.com","password":"correct-horse-battery-staple"}' "$identity/internal/auth/login")
access=$(jq -r .accessToken <<<"$login")

test "$(status -H 'X-User-Id: c7663093-4fc3-4971-bb41-cee9c4bdfa68' "$journal/internal/diaries")" = 401
test "$(status -H 'Authorization: Bearer invalid' "$journal/internal/diaries")" = 401
test "$(status -H "Authorization: Bearer $access" "$journal/internal/diaries")" = 200
test "$(status -H "Authorization: Bearer $access" -H 'Content-Type: application/json' \
  -d '{"localDate":"2026-07-11","content":"JWT-owned quick note"}' "$journal/internal/quick-note")" = 201

echo 'jwt ownership smoke: ok'
