#!/usr/bin/env bash
set -euo pipefail
identity=http://127.0.0.1:5100
journal=http://127.0.0.1:5101
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

access=$(curl -sS -H 'Content-Type: application/json' -d '{"email":"owner@example.com","password":"correct-horse-battery-staple"}' "$identity/internal/auth/login" | jq -r .accessToken)
auth="Authorization: Bearer $access"
post() { curl -sS -H "$auth" -H 'Content-Type: application/json' -H "Idempotency-Key: $1" -d "$2" "$3"; }
status() { curl -sS -o /dev/null -w '%{http_code}' -H "$auth" -H 'Content-Type: application/json' -H "Idempotency-Key: $1" -d "$2" "$3"; }

diary_payload='{"localDate":"2026-07-12","title":"Idempotent diary","content":"once"}'
post diary-key "$diary_payload" "$journal/internal/diaries" >"$tmp/diary-1" &
post diary-key "$diary_payload" "$journal/internal/diaries" >"$tmp/diary-2" &
wait
cmp "$tmp/diary-1" "$tmp/diary-2"
diary_id=$(jq -r .id "$tmp/diary-1")
test "$(curl -sS -H "$auth" "$journal/internal/diaries" | jq --arg id "$diary_id" '[.items[] | select(.id == $id)] | length')" = 1
test "$(status diary-key '{"localDate":"2026-07-12","title":"Changed","content":"once"}' "$journal/internal/diaries")" = 409

note_payload=$(jq -nc --arg id "$diary_id" '{localDate:"2026-07-12",content:"append exactly once",targetDiaryId:$id}')
post note-key "$note_payload" "$journal/internal/quick-note" >"$tmp/note-1" &
post note-key "$note_payload" "$journal/internal/quick-note" >"$tmp/note-2" &
wait
cmp "$tmp/note-1" "$tmp/note-2"
test "$(curl -sS -H "$auth" "$journal/internal/diaries/$diary_id" | jq -r '.content | [scan("append exactly once")] | length')" = 1
test "$(status note-key "$(jq -nc --arg id "$diary_id" '{localDate:"2026-07-12",content:"different",targetDiaryId:$id}')" "$journal/internal/quick-note")" = 409

transaction_payload='{"symbol":"AAPL","side":"buy","quantity":2,"price":200,"currency":"USD","tradedAt":"2026-07-12T08:00:00Z","notes":"once"}'
post transaction-key "$transaction_payload" "$journal/internal/diaries/$diary_id/transactions" >"$tmp/transaction-1" &
post transaction-key "$transaction_payload" "$journal/internal/diaries/$diary_id/transactions" >"$tmp/transaction-2" &
wait
cmp "$tmp/transaction-1" "$tmp/transaction-2"
transaction_id=$(jq -r .id "$tmp/transaction-1")
test "$(curl -sS -H "$auth" "$journal/internal/diaries/$diary_id/transactions" | jq --arg id "$transaction_id" '[.items[] | select(.id == $id)] | length')" = 1
test "$(status transaction-key '{"symbol":"AAPL","side":"buy","quantity":3,"price":200,"currency":"USD","tradedAt":"2026-07-12T08:00:00Z"}' "$journal/internal/diaries/$diary_id/transactions")" = 409

# The header stays optional.
test "$(curl -sS -o /dev/null -w '%{http_code}' -H "$auth" -H 'Content-Type: application/json' -d '{"localDate":"2026-07-12","title":"No key","content":""}' "$journal/internal/diaries")" = 201

echo 'journal idempotency smoke: ok'
