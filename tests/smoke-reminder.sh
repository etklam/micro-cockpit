#!/usr/bin/env bash
set -euo pipefail
identity=http://127.0.0.1:5100
journal=http://127.0.0.1:5101
reminder=http://127.0.0.1:5104
key=${SERVICE_KEY:-local-service-key}
service_key="X-Service-Key: $key"
status() { curl -sS -o /dev/null -w '%{http_code}' "$@"; }
rejected() { case "$1" in 401|403) return 0;; *) return 1;; esac; }

login=$(curl -sS -H 'Content-Type: application/json' -d '{"email":"owner@example.com","password":"correct-horse-battery-staple"}' "$identity/internal/auth/login")
access=$(jq -r .accessToken <<<"$login"); auth="Authorization: Bearer $access"
diary=$(curl -sS -H "$auth" -H 'Content-Type: application/json' -d '{"localDate":"2026-07-09","title":"Reminder check","content":""}' "$journal/internal/diaries")
diary_id=$(jq -r .id <<<"$diary")

test "$(status -H "$auth" -H 'Content-Type: application/json' -d '{"diaryId":"00000000-0000-0000-0000-000000000001","startLocalDate":"2026-07-09","localTime":"00:00:00","timezone":"UTC","repeatMode":"none"}' "$reminder/internal/diary-alerts")" = 404
alert=$(curl -sS -H "$auth" -H 'Content-Type: application/json' -d "{\"diaryId\":\"$diary_id\",\"startLocalDate\":\"2026-07-09\",\"localTime\":\"00:00:00\",\"timezone\":\"UTC\",\"repeatMode\":\"none\"}" "$reminder/internal/diary-alerts")
alert_id=$(jq -r .id <<<"$alert")

rejected "$(status -X POST "$reminder/internal/worker/run")"
rejected "$(status -H 'X-Service-Key: wrong' -X POST "$reminder/internal/worker/run")"
rejected "$(status -H "$auth" -X POST "$reminder/internal/worker/run")"

curl -sS -o /dev/null -H "$service_key" -X POST "$reminder/internal/worker/run" &
first=$!
curl -sS -o /dev/null -H "$service_key" -X POST "$reminder/internal/worker/run" &
second=$!
wait "$first" "$second"
attempts=$(docker exec micro-cockpit-postgres-1 psql -U trade_diary -d trade_diary -Atc "select count(*) from reminder.reminder_delivery_attempts where diary_alert_id='$alert_id'")
test "$attempts" = 1
state=$(curl -sS -H "$auth" "$reminder/internal/diary-alerts" | jq -r ".items[] | select(.id==\"$alert_id\") | .status")
test "$state" = expired

echo 'reminder smoke: ok'
