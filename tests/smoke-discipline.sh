#!/usr/bin/env bash
set -euo pipefail
identity=http://127.0.0.1:5100
discipline=http://127.0.0.1:5103
status() { curl -sS -o /dev/null -w '%{http_code}' "$@"; }

login=$(curl -sS -H 'Content-Type: application/json' -d '{"email":"owner@example.com","password":"correct-horse-battery-staple"}' "$identity/internal/auth/login")
access=$(jq -r .accessToken <<<"$login"); auth="Authorization: Bearer $access"
curl -sS -o /dev/null -H "$auth" -H 'Content-Type: application/json' -d '{"content":"Size risk before seeking reward."}' "$discipline/internal/disciplines"
curl -sS -o /dev/null -H "$auth" -H 'Content-Type: application/json' -d '{"content":"Review the process, not only the outcome."}' "$discipline/internal/disciplines"

today1=$(curl -sS -H "$auth" "$discipline/internal/disciplines/today?date=2026-07-11" | jq -r .id)
curl -sS -o /dev/null -H "$auth" "$discipline/internal/disciplines/random"
today2=$(curl -sS -H "$auth" "$discipline/internal/disciplines/today?date=2026-07-11" | jq -r .id)
test "$today1" = "$today2"

ids=$(curl -sS -H "$auth" "$discipline/internal/disciplines" | jq -c '[.items[].id] | reverse')
test "$(status -H "$auth" -H 'Content-Type: application/json' -d "{\"ids\":$ids}" "$discipline/internal/disciplines/reorder")" = 204
test "$(status -H "$auth" -H 'Content-Type: application/json' -d '{"ids":[]}' "$discipline/internal/disciplines/reorder")" = 400

echo 'discipline smoke: ok'
