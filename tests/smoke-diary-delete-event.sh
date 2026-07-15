#!/usr/bin/env bash
set -euo pipefail
: "${TEST_PASSWORD:?set TEST_PASSWORD}"
: "${INTERNAL_SERVICE_KEY:?set INTERNAL_SERVICE_KEY}"
identity=http://127.0.0.1:5100
journal=http://127.0.0.1:5101
reminder=http://127.0.0.1:5104
psql=(docker exec micro-cockpit-postgres-1 psql -U trade_diary -d trade_diary -Atc)

access=$(curl -sS -H 'Content-Type: application/json' -d "{\"email\":\"owner@example.com\",\"password\":\"${TEST_PASSWORD}\"}" "$identity/internal/auth/login" | jq -r .accessToken)
auth="Authorization: Bearer $access"
diary_id=$(curl -sS -H "$auth" -H 'Content-Type: application/json' -d '{"localDate":"2026-07-11","title":"Delete event check","content":""}' "$journal/internal/diaries" | jq -r .id)
alert_id=$(curl -sS -H "$auth" -H 'Content-Type: application/json' -d "{\"diaryId\":\"$diary_id\",\"startLocalDate\":\"2026-12-01\",\"localTime\":\"09:00:00\",\"timezone\":\"UTC\",\"repeatMode\":\"none\"}" "$reminder/internal/diary-alerts" | jq -r .id)

test "$(curl -sS -o /dev/null -w '%{http_code}' -X DELETE -H "$auth" "$journal/internal/diaries/$diary_id")" = 204
for _ in {1..20}; do
  state=$("${psql[@]}" "select status from reminder.diary_alerts where id='$alert_id'")
  test "$state" = expired && break
  sleep .25
done
test "$state" = expired

event_id=$("${psql[@]}" "select event_id from journal.outbox_events where payload->>'diaryId'='$diary_id'")
user_id=$("${psql[@]}" "select payload->>'userId' from journal.outbox_events where event_id='$event_id'")
payload="{\"eventId\":\"$event_id\",\"eventType\":\"DiaryDeleted.v1\",\"version\":1,\"payload\":{\"diaryId\":\"$diary_id\",\"userId\":\"$user_id\"}}"
test "$(curl -sS -o /dev/null -w '%{http_code}' -H "X-Service-Key: ${INTERNAL_SERVICE_KEY}" -H 'Content-Type: application/json' -d "$payload" "$reminder/internal/events/diary-deleted")" = 204
test "$("${psql[@]}" "select count(*) from reminder.inbox_events where event_id='$event_id'")" = 1

echo 'diary delete event smoke: ok'
