#!/usr/bin/env bash
set -euo pipefail

edge=${EDGE_URL:-http://127.0.0.1:5099}
email=${TEST_EMAIL:-owner@example.com}
password=${TEST_PASSWORD:?set TEST_PASSWORD}

for command in curl docker jq python3; do
  command -v "$command" >/dev/null || { echo "missing command: $command" >&2; exit 1; }
done

tmp=$(mktemp -d)
rotation_code=E2E_RELEASE
rotation_run_id=00000000-0000-0000-0000-00000000e2e1
rotation_symbols=(E2ERSPY E2ERXLK E2ERXLE)

db_exec() {
  docker compose exec -T postgres psql -v ON_ERROR_STOP=1 -U trade_diary -d trade_diary -c "$1"
}

db_query() {
  docker compose exec -T postgres psql -v ON_ERROR_STOP=1 -U trade_diary -d trade_diary -Atc "$1"
}

cleanup_rotation() {
  db_exec "
    DELETE FROM rotation.market_rotation_universes WHERE code='$rotation_code';
    DELETE FROM market.daily_bars WHERE provider_run_id='$rotation_run_id';
    DELETE FROM market.provider_runs WHERE id='$rotation_run_id';
    DELETE FROM market.symbols WHERE symbol IN ('${rotation_symbols[0]}','${rotation_symbols[1]}','${rotation_symbols[2]}');
  " >/dev/null 2>&1 || true
}

trap 'cleanup_rotation; rm -rf "$tmp"' EXIT

status() {
  curl -sS -o /dev/null -w '%{http_code}' "$@"
}

today=$(python3 -c 'from datetime import datetime,timezone; print(datetime.now(timezone.utc).date().isoformat())')
due_date=$(python3 -c 'from datetime import datetime,timedelta,timezone; d=datetime.now(timezone.utc).date()-timedelta(days=1); d-=timedelta(days=max(0,d.weekday()-4)); print(d.isoformat())')
future_date=$(python3 -c 'from datetime import datetime,timedelta,timezone; d=datetime.now(timezone.utc).date()+timedelta(days=7); d+=timedelta(days=(7-d.weekday())%7); print(d.isoformat())')
run_tag=$(python3 -c 'import os,time; print(f"{int(time.time())}-{os.getpid()}")')

login=$(curl -fsS -H 'Content-Type: application/json' \
  -d "$(jq -nc --arg email "$email" --arg password "$password" '{email:$email,password:$password}')" \
  "$edge/api/auth/login")
access=$(jq -er .accessToken <<<"$login")
auth="Authorization: Bearer $access"

# Required BFF composition and response correlation stay on the public Edge path.
test "$(status "$edge/api/app/dashboard")" = 401
bff_diary=$(curl -fsS -H "$auth" -H 'Content-Type: application/json' \
  -H "Idempotency-Key: e2e-bff-diary-$run_tag" \
  -d "$(jq -nc --arg date "$today" '{localDate:$date,title:"E2E BFF diary",content:"BFF composition"}')" \
  "$edge/api/app/diaries")
bff_diary_id=$(jq -er .id <<<"$bff_diary")
dashboard=$(curl -fsS -H "$auth" "$edge/api/app/dashboard")
test "$(jq -er --arg date "$today" '.localDate == $date and .diary.writtenToday == true and .capabilities.alerts == "available"' <<<"$dashboard")" = true
curl -sS -D "$tmp/bff.headers" -o /dev/null -H "$auth" -H 'X-Correlation-ID: e2e-release-correlation' "$edge/api/app/dashboard"
test "$(awk 'tolower($1)=="x-correlation-id:" {gsub("\r", "", $2); print $2}' "$tmp/bff.headers")" = e2e-release-correlation

# Reminder delivery is exercised through Edge; the hosted worker is observed through Compose's database service.
reminder_diary=$(curl -fsS -H "$auth" -H 'Content-Type: application/json' \
  -H "Idempotency-Key: e2e-reminder-diary-$run_tag" \
  -d "$(jq -nc --arg date "$today" '{localDate:$date,title:"E2E reminder diary",content:"Reminder delivery"}')" \
  "$edge/api/app/diaries")
reminder_diary_id=$(jq -er .id <<<"$reminder_diary")
reminder=$(curl -fsS -H "$auth" -H 'Content-Type: application/json' \
  -d "$(jq -nc --arg diary "$reminder_diary_id" --arg date "$due_date" '{diaryId:$diary,startLocalDate:$date,localTime:"00:00:00",timezone:"UTC",repeatMode:"none"}')" \
  "$edge/api/app/diary-alerts")
reminder_id=$(jq -er .id <<<"$reminder")
test "$(jq -r .status <<<"$reminder")" = active

for ((attempt=0; attempt<60; attempt++)); do
  reminder_status=$(db_query "SELECT status FROM reminder.diary_alerts WHERE id='$reminder_id'")
  deliveries=$(db_query "SELECT count(*) FROM reminder.reminder_delivery_attempts WHERE diary_alert_id='$reminder_id' AND status='delivered'")
  if [ "$reminder_status" = expired ] && [ "$deliveries" = 1 ]; then break; fi
  sleep 1
done
test "$reminder_status" = expired
test "$deliveries" = 1

# Diary deletion publishes a versioned event and expires the linked active reminder through the normal network hop.
event_diary=$(curl -fsS -H "$auth" -H 'Content-Type: application/json' \
  -H "Idempotency-Key: e2e-event-diary-$run_tag" \
  -d "$(jq -nc --arg date "$today" '{localDate:$date,title:"E2E event diary",content:"Delete event"}')" \
  "$edge/api/app/diaries")
event_diary_id=$(jq -er .id <<<"$event_diary")
event_reminder=$(curl -fsS -H "$auth" -H 'Content-Type: application/json' \
  -d "$(jq -nc --arg diary "$event_diary_id" --arg date "$future_date" '{diaryId:$diary,startLocalDate:$date,localTime:"00:00:00",timezone:"UTC",repeatMode:"none"}')" \
  "$edge/api/app/diary-alerts")
event_reminder_id=$(jq -er .id <<<"$event_reminder")
test "$(status -X DELETE -H "$auth" "$edge/api/app/diaries/$event_diary_id")" = 204

event_id=
event_status=
event_published=
event_inbox=
for ((attempt=0; attempt<30; attempt++)); do
  event_id=$(db_query "SELECT event_id::text FROM journal.outbox_events WHERE payload->>'diaryId'='$event_diary_id' ORDER BY occurred_at DESC LIMIT 1")
  event_status=$(db_query "SELECT status FROM reminder.diary_alerts WHERE id='$event_reminder_id'")
  if [ -n "$event_id" ]; then
    event_published=$(db_query "SELECT count(*) FROM journal.outbox_events WHERE event_id='$event_id' AND published_at IS NOT NULL")
    event_inbox=$(db_query "SELECT count(*) FROM reminder.inbox_events WHERE event_id='$event_id' AND processed_at IS NOT NULL")
  else
    event_published=0
    event_inbox=0
  fi
  if [ "$event_status" = expired ] && [ "$event_published" = 1 ] && [ "$event_inbox" = 1 ]; then break; fi
  sleep 1
done
test -n "$event_id"
test "$event_status" = expired
test "$event_published" = 1
test "$event_inbox" = 1

# Rotation uses fixed fixture data and all public mutations/reads go through Edge.
cleanup_rotation
docker compose exec -T postgres psql -v ON_ERROR_STOP=1 -U trade_diary -d trade_diary <<SQL >/dev/null
BEGIN;
INSERT INTO market.symbols(symbol,name,exchange,currency,timezone) VALUES
 ('${rotation_symbols[0]}','E2E Rotation Index','TEST','USD','UTC'),
 ('${rotation_symbols[1]}','E2E Rotation Technology','TEST','USD','UTC'),
 ('${rotation_symbols[2]}','E2E Rotation Energy','TEST','USD','UTC');
INSERT INTO market.provider_runs(id,provider,started_at,completed_at,status,rows_received)
VALUES('$rotation_run_id','e2e-rotation',now(),now(),'succeeded',603);
INSERT INTO market.daily_bars(symbol,trading_date,open,high,low,close,volume,provider,provider_run_id,published_at)
SELECT symbol,date '2025-01-01'+day,base+slope*day,base+slope*day,base+slope*day,base+slope*day,1000000,'e2e-rotation','$rotation_run_id',now()
FROM (VALUES('${rotation_symbols[0]}',100,1),('${rotation_symbols[1]}',100,2),('${rotation_symbols[2]}',300,-1)) v(symbol,base,slope)
CROSS JOIN generate_series(0,200) g(day);
COMMIT;
SQL

universe=$(curl -fsS -H "$auth" -H 'Content-Type: application/json' \
  -d '{"code":"E2E_RELEASE","name":"E2E Release Universe","rankScope":"universe"}' \
  "$edge/api/app/rotation/universes")
universe_id=$(jq -er .id <<<"$universe")
curl -fsS -o /dev/null -X PUT -H "$auth" -H 'Content-Type: application/json' \
  -d "$(jq -nc --arg a "${rotation_symbols[0]}" --arg b "${rotation_symbols[1]}" --arg c "${rotation_symbols[2]}" '[{symbol:$a,label:"E2E Index",sector:"Index"},{symbol:$b,label:"E2E Technology",sector:"Sector"},{symbol:$c,label:"E2E Energy",sector:"Sector"}]')" \
  "$edge/api/app/rotation/universes/$universe_id/symbols"
calculation=$(curl -fsS -X POST -H "$auth" "$edge/api/app/rotation/universes/$universe_id/calculate?date=2025-07-20")
test "$(jq -r .status <<<"$calculation")" = completed
test "$(jq -r .formulaVersion <<<"$calculation")" = rotation-v1
rotation=$(curl -fsS -H "$auth" "$edge/api/app/rotation/monitor?universe=$rotation_code&date=2025-07-20")
test "$(jq -r .formulaVersion <<<"$rotation")" = rotation-v1
test "$(jq -r '.status' <<<"$rotation")" = ok
test "$(jq -r '.marketState.state' <<<"$rotation")" = risk_on
test "$(jq '[.etfs[] | select(.status=="ok")] | length' <<<"$rotation")" = 3
test "$(jq -r '.etfs[0].symbol' <<<"$rotation")" = "${rotation_symbols[1]}"

echo 'release paths smoke: BFF, reminder delivery, delete event, rotation ok'
