#!/usr/bin/env bash
set -euo pipefail

identity=${IDENTITY_URL:-http://127.0.0.1:5100}
stock=${STOCK_RESEARCH_URL:-http://127.0.0.1:5105}
status() { curl -sS -o /dev/null -w '%{http_code}' "$@"; }
login() { curl -sS -H 'Content-Type: application/json' -d "{\"email\":\"$1\",\"password\":\"correct-horse-battery-staple\"}" "$identity/internal/auth/login"; }

owner_access=$(login owner@example.com | jq -r .accessToken)
owner_auth="Authorization: Bearer $owner_access"

created=$(curl -sS -H "$owner_auth" -H 'Content-Type: application/json' \
  -d '{"symbol":"smkt","name":"Smoke Test Corp","exchange":"NASDAQ","assetType":"stock"}' "$stock/internal/stocks")
stock_id=$(jq -r .id <<<"$created")
if test "$stock_id" = null; then stock_id=$(curl -sS -H "$owner_auth" "$stock/internal/stocks/SMKT" | jq -r .id); fi
test "$(status -H "$owner_auth" -H 'Content-Type: application/json' -d '{"symbol":"ETF-X","name":"Not a stock","assetType":"etf"}' "$stock/internal/stocks")" = 400

watch_status=$(status -H "$owner_auth" -X POST "$stock/internal/watchlist/$stock_id")
test "$watch_status" = 201 -o "$watch_status" = 204
curl -sS -H "$owner_auth" -H 'Content-Type: application/json' -X PUT -d '{"content":"First thesis"}' "$stock/internal/stocks/$stock_id/note" | jq -e '.content == "First thesis"' >/dev/null
curl -sS -H "$owner_auth" -H 'Content-Type: application/json' -X PUT -d '{"content":"Updated thesis"}' "$stock/internal/stocks/$stock_id/note" | jq -e '.content == "Updated thesis"' >/dev/null

original=$(curl -sS -H "$owner_auth" -H 'Content-Type: application/json' -d \
  '{"eventTime":"2026-07-12T08:00:00Z","sourceType":"manual","title":"Earnings","content":"Original evidence"}' "$stock/internal/stocks/$stock_id/timeline")
original_id=$(jq -r .id <<<"$original")
correction=$(curl -sS -H "$owner_auth" -H 'Content-Type: application/json' -d \
  '{"sourceType":"correction","title":"Correction","content":"Corrected evidence"}' "$stock/internal/timeline/$original_id/corrections")
test "$(jq -r .correctionOfId <<<"$correction")" = "$original_id"
test "$(status -H "$owner_auth" -X PUT -H 'Content-Type: application/json' -d '{"title":"mutate"}' "$stock/internal/timeline/$original_id")" = 405
test "$(status -H "$owner_auth" -X DELETE "$stock/internal/timeline/$original_id")" = 405

other_email="stock-smoke-$(date +%s)@example.com"
curl -sS -H 'X-Registration-Key: change-me-before-exposing' -H 'Content-Type: application/json' -d \
  "{\"email\":\"$other_email\",\"password\":\"correct-horse-battery-staple\",\"displayName\":\"Other\",\"timezone\":\"UTC\",\"baseCurrency\":\"USD\"}" \
  "$identity/internal/auth/register" >/dev/null
other_access=$(login "$other_email" | jq -r .accessToken); other_auth="Authorization: Bearer $other_access"
test "$(status -H "$other_auth" "$stock/internal/timeline/$original_id")" = 404
test "$(status -H "$other_auth" "$stock/internal/stocks/$stock_id/note")" = 404
test "$(curl -sS -H "$other_auth" "$stock/internal/stocks/$stock_id/timeline" | jq '.items | length')" = 0

echo 'stock research smoke: ok'
