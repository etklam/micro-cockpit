#!/usr/bin/env bash
set -euo pipefail
trap 'echo "release smoke failed at line $LINENO" >&2' ERR

edge=${EDGE_URL:-http://127.0.0.1:5099}
email=${TEST_EMAIL:-owner@example.com}
password=${TEST_PASSWORD:?set TEST_PASSWORD}
instrument_id=00000000-0000-0000-0000-00000000e2e1
provider_run_id=00000000-0000-0000-0000-00000000e2e2
symbol=E2ECLOSE
today=$(date -u +%F)
run_tag="$(date +%s)-$$"

for command in curl docker jq; do
  command -v "$command" >/dev/null || { echo "missing command: $command" >&2; exit 1; }
done

db_exec() {
  docker compose exec -T postgres psql -v ON_ERROR_STOP=1 -U trade_diary -d trade_diary -c "$1" >/dev/null
}

cleanup() {
  db_exec "DELETE FROM market.daily_bars WHERE provider_run_id='$provider_run_id';
    DELETE FROM market.provider_runs WHERE id='$provider_run_id';
    DELETE FROM market.symbols WHERE symbol='$symbol';
    DELETE FROM market.instruments WHERE id='$instrument_id';" 2>/dev/null || true
}
trap cleanup EXIT
cleanup
db_exec "INSERT INTO market.instruments(id) VALUES ('$instrument_id');
  INSERT INTO market.symbols(symbol,name,exchange,currency,timezone,instrument_id)
    VALUES ('$symbol','E2E Daily Close','TEST','USD','UTC','$instrument_id');
  INSERT INTO market.provider_runs(id,provider,started_at,completed_at,status,rows_received)
    VALUES ('$provider_run_id','e2e',now(),now(),'succeeded',1);
  INSERT INTO market.daily_bars(symbol,trading_date,open,high,low,close,adjusted_close,volume,provider,provider_run_id,published_at)
    VALUES ('$symbol',current_date,100,105,99,104,103,1000,'e2e','$provider_run_id',now());"

access=$(curl -fsS -H 'Content-Type: application/json' \
  -d "$(jq -nc --arg email "$email" --arg password "$password" '{email:$email,password:$password}')" \
  "$edge/api/auth/login" | jq -er .accessToken)
auth="Authorization: Bearer $access"

quick=$(curl -fsS -H "$auth" -H 'Content-Type: application/json' \
  -H "Idempotency-Key: release-observation-$run_tag" \
  -d '{"content":"Breadth weakened while the test instrument held support."}' \
  "$edge/api/app/quick-observations")
observation_id=$(jq -er .marketObservationId <<<"$quick")
update_id=$(jq -er .observationUpdateId <<<"$quick")

curl -fsS -X PUT -H "$auth" -H 'Content-Type: application/json' \
  -d "$(jq -nc --arg instrument "$instrument_id" --arg symbol "$symbol" '{
    content:"Breadth weakened while the test instrument held support.",
    signal:"Daily breadth weakened.", interpretation:"Support may still matter.",
    tags:["release-smoke"], primarySubject:{type:"instrument",instrumentId:$instrument,market:"US",symbol:$symbol,displayName:"E2E Daily Close"}
  }')" "$edge/api/app/observation-updates/$update_id" >/dev/null
today_observation=$(curl -fsS -H "$auth" "$edge/api/app/market-observations/today")
test "$(jq -er --arg id "$update_id" '.updates[] | select(.id == $id) | .primarySubject.dailyCloseStatus == "available" and .primarySubject.dailyClose.rawClose == 104 and .primarySubject.dailyClose.adjustedClose == 103' <<<"$today_observation")" = true

expectation=$(curl -fsS -H "$auth" -H 'Content-Type: application/json' \
  -H "Idempotency-Key: release-expectation-$run_tag" \
  -d '{"expectedBehavior":"Support remains visible.","deadline":"2000-01-01T00:00:00Z","deadlinePreset":null,"invalidationCondition":"Daily Close below 95.","confidence":"medium","market":"US"}' \
  "$edge/api/app/observation-updates/$update_id/expectations")
expectation_id=$(jq -er .id <<<"$expectation")
review=$(curl -fsS -X PUT -H "$auth" -H 'Content-Type: application/json' \
  -d '{"outcome":"indeterminate","reasoningQuality":"mixed","explanation":"The retained evidence was incomplete.","systemIssueKeys":[],"systemStrengthKeys":[],"customLabelIds":[]}' \
  "$edge/api/app/expectations/$expectation_id/review")
test "$(jq -er '.outcome == "indeterminate" and .reasoningQuality == "mixed"' <<<"$review")" = true

watch_code=$(curl -sS -o /dev/null -w '%{http_code}' -X POST -H "$auth" "$edge/api/app/watchlist/$instrument_id")
test "$watch_code" = 201 || test "$watch_code" = 204
watch=$(curl -fsS -H "$auth" "$edge/api/app/watchlist")
test "$(jq -er --arg id "$instrument_id" '[.items[] | select(.instrumentId == $id)] | length == 1' <<<"$watch")" = true
curl -fsS -X PUT -H "$auth" -H 'Content-Type: application/json' \
  -d '{"note":"Watch the completed-session evidence."}' \
  "$edge/api/app/watchlist/$instrument_id/note" >/dev/null

agent=$(curl -fsS -H "$auth" -H 'Content-Type: application/json' \
  -d "$(jq -nc --arg name "release-agent-$run_tag" '{name:$name,displayName:"Release Agent",timezone:"UTC",baseCurrency:"USD",scopes:["journal:read","journal:write","agent:read"],expiresAt:null}')" \
  "$edge/api/app/agents")
agent_id=$(jq -er .userId <<<"$agent")
agent_token=$(jq -er .apiToken <<<"$agent")
agent_access=$(curl -fsS -H 'Content-Type: application/json' \
  -d "$(jq -nc --arg apiKey "$agent_token" '{apiKey:$apiKey}')" \
  "$edge/api/auth/api-key/token" | jq -er .accessToken)
grant=$(curl -fsS -H "$auth" -H 'Content-Type: application/json' \
  -d "$(jq -nc --arg agent "$agent_id" --arg date "$today" --arg instrument "$instrument_id" '{
    agentUserId:$agent,mode:"fixed",from:$date,to:$date,instrumentId:$instrument,expiresAt:null
  }')" "$edge/api/app/access-grants")
test "$(jq -er --arg agent "$agent_id" '.agentUserId == $agent' <<<"$grant")" = true

records=$(curl -fsS -H "Authorization: Bearer $agent_access" \
  "$edge/api/agent/journal-records?instrumentId=$instrument_id")
test "$(jq -er '.items | length > 0' <<<"$records")" = true
cursor=$(jq -er .syncCursor <<<"$records")

agent_quick=$(curl -fsS -H "Authorization: Bearer $agent_access" -H 'Content-Type: application/json' \
  -H "Idempotency-Key: release-agent-observation-$run_tag" \
  -d '{"content":"Agent retained an independent view."}' \
  "$edge/api/app/quick-observations")
agent_update_id=$(jq -er .observationUpdateId <<<"$agent_quick")
curl -fsS -X PUT -H "Authorization: Bearer $agent_access" -H 'Content-Type: application/json' \
  -d "$(jq -nc --arg instrument "$instrument_id" --arg symbol "$symbol" '{
    content:"Agent retained an independent view.",
    signal:"Agent signal.", interpretation:"Agent interpretation.",
    primarySubject:{type:"instrument",instrumentId:$instrument,market:"US",symbol:$symbol,displayName:"E2E Daily Close"}
  }')" "$edge/api/app/observation-updates/$agent_update_id" >/dev/null
curl -fsS -H "Authorization: Bearer $agent_access" -H 'Content-Type: application/json' \
  -H "Idempotency-Key: release-agent-expectation-$run_tag" \
  -d '{"expectedBehavior":"Agent support remains visible.","deadline":"2099-01-01T00:00:00Z","deadlinePreset":null,"invalidationCondition":"Daily Close below 90.","confidence":"high","market":"US"}' \
  "$edge/api/app/observation-updates/$agent_update_id/expectations" >/dev/null
comparison=$(curl -fsS -H "$auth" \
  "$edge/api/app/comparison?agentUserId=$agent_id&from=$today&to=$today&instrumentId=$instrument_id")
test "$(jq -er --arg agent "$agent_id" '
  .human.ownerType == "human" and .agent.ownerType == "agent"
  and .agent.ownerId == $agent and (.human.observations | length) > 0
  and (.agent.observations | length) > 0' <<<"$comparison")" = true

test "$(curl -sS -o /dev/null -w '%{http_code}' -X DELETE -H "$auth" \
  "$edge/api/app/observation-updates/$update_id")" = 204
encoded_cursor=$(jq -nr --arg value "$cursor" '$value|@uri')
changes=$(curl -fsS -H "Authorization: Bearer $agent_access" \
  "$edge/api/agent/journal-changes?cursor=$encoded_cursor")
test "$(jq -er --arg id "$update_id" '[.items[] | select(.recordId == $id and .recordType == "observation_update" and has("deletedAt"))] | length == 1' <<<"$changes")" = true

for route in dashboard diaries diary-alerts diary-review-summary price-alerts rotation/monitor partners stocks; do
  test "$(curl -sS -o /dev/null -w '%{http_code}' -H "$auth" "$edge/api/app/$route")" = 404
done
test "$(curl -sS -o /dev/null -w '%{http_code}' -H "$auth" "$edge/api/content/posts")" = 404
test "$(curl -sS -o /dev/null -w '%{http_code}' -H "$auth" "$edge/api/admin/operations/audit")" = 404

echo 'release smoke: observation, expectation, review, watchlist, Daily Close, agent grant/comparison, deletion sync, retired routes ok'
