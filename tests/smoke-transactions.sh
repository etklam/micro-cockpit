#!/usr/bin/env bash
set -euo pipefail
identity=http://127.0.0.1:5100
journal=http://127.0.0.1:5101
status() { curl -sS -o /dev/null -w '%{http_code}' "$@"; }

login=$(curl -sS -H 'Content-Type: application/json' -d '{"email":"owner@example.com","password":"correct-horse-battery-staple"}' "$identity/internal/auth/login")
access=$(jq -r .accessToken <<<"$login")
auth="Authorization: Bearer $access"
diary=$(curl -sS -H "$auth" -H 'Content-Type: application/json' -d '{"localDate":"2026-07-11","title":"Transaction check","content":""}' "$journal/internal/diaries")
diary_id=$(jq -r .id <<<"$diary")

test "$(status -H "$auth" -H 'Content-Type: application/json' -d '{"symbol":"AAPL","side":"buy","quantity":0,"price":200,"currency":"USD","tradedAt":"2026-07-11T08:00:00Z"}' "$journal/internal/diaries/$diary_id/transactions")" = 400
transaction=$(curl -sS -H "$auth" -H 'Content-Type: application/json' -d '{"symbol":"aapl","side":"buy","quantity":2,"price":200,"currency":"usd","tradedAt":"2026-07-11T08:00:00Z","notes":"Within plan"}' "$journal/internal/diaries/$diary_id/transactions")
transaction_id=$(jq -r .id <<<"$transaction")
test "$(jq -r '.symbol + ":" + .currency' <<<"$transaction")" = 'AAPL:USD'
test "$(status -X PUT -H "$auth" -H 'Content-Type: application/json' -d '{"symbol":"AAPL","side":"sell","quantity":1,"price":210,"currency":"USD","tradedAt":"2026-07-11T09:00:00Z"}' "$journal/internal/diaries/$diary_id/transactions/$transaction_id")" = 204
test "$(status -X DELETE -H "$auth" "$journal/internal/diaries/$diary_id/transactions/$transaction_id")" = 204
test "$(curl -sS -H "$auth" "$journal/internal/diaries/$diary_id/transactions" | jq '.items | length')" = 0
test "$(status -H "$auth" -H 'Content-Type: application/json' -d '{"symbol":"AAPL","side":"buy","quantity":1,"price":200,"currency":"USD","tradedAt":"2026-07-11T08:00:00Z"}' "$journal/internal/diaries/00000000-0000-0000-0000-000000000001/transactions")" = 404

echo 'transactions smoke: ok'
