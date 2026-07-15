#!/usr/bin/env bash
set -euo pipefail
: "${TEST_PASSWORD:?set TEST_PASSWORD}"
identity=http://127.0.0.1:5100
rotation=http://127.0.0.1:5107
psql=(docker exec -i micro-cockpit-postgres-1 psql -U trade_diary -d trade_diary -v ON_ERROR_STOP=1)
run_id=00000000-0000-0000-0000-00000000a010
cleanup() {
  "${psql[@]}" -c "DELETE FROM rotation.market_rotation_universes WHERE code='ROTATION_SMOKE'; DELETE FROM market.daily_bars WHERE provider_run_id='$run_id'; DELETE FROM market.provider_runs WHERE id='$run_id'; DELETE FROM market.symbols WHERE symbol IN ('ROTSPY','ROTXLK','ROTXLE') AND NOT EXISTS (SELECT 1 FROM market.daily_bars b WHERE b.symbol=market.symbols.symbol);" >/dev/null 2>&1 || true
}
trap cleanup EXIT
access=$(curl -sS -H 'Content-Type: application/json' -d "{\"email\":\"owner@example.com\",\"password\":\"${TEST_PASSWORD}\"}" "$identity/internal/auth/login" | jq -r .accessToken)
auth="Authorization: Bearer $access"

"${psql[@]}" <<'SQL' >/dev/null
DELETE FROM rotation.market_rotation_universes WHERE code='ROTATION_SMOKE';
DELETE FROM market.daily_bars WHERE provider_run_id='00000000-0000-0000-0000-00000000a010';
DELETE FROM market.provider_runs WHERE id='00000000-0000-0000-0000-00000000a010';
INSERT INTO market.symbols(symbol,name,exchange,currency,timezone) VALUES
 ('ROTSPY','Rotation Smoke Index','TEST','USD','UTC'),
 ('ROTXLK','Rotation Smoke Technology','TEST','USD','UTC'),
 ('ROTXLE','Rotation Smoke Energy','TEST','USD','UTC')
ON CONFLICT(symbol) DO NOTHING;
INSERT INTO market.provider_runs(id,provider,started_at,completed_at,status,rows_received)
VALUES('00000000-0000-0000-0000-00000000a010','rotation-smoke',now(),now(),'succeeded',603);
INSERT INTO market.daily_bars(symbol,trading_date,open,high,low,close,volume,provider,provider_run_id,published_at)
SELECT symbol,date '2025-01-01'+day,base+slope*day,base+slope*day,base+slope*day,base+slope*day,1000000,'rotation-smoke','00000000-0000-0000-0000-00000000a010',now()
FROM (VALUES('ROTSPY',100,1),('ROTXLK',100,2),('ROTXLE',300,-1))v(symbol,base,slope)
CROSS JOIN generate_series(0,200)g(day);
SQL

universe=$(curl -sS -H "$auth" -H 'Content-Type: application/json' -d '{"code":"ROTATION_SMOKE","name":"Rotation Smoke ETFs","rankScope":"universe"}' "$rotation/internal/rotation/universes")
id=$(jq -r .id <<<"$universe")
curl -fsS -o /dev/null -X PUT -H "$auth" -H 'Content-Type: application/json' -d '[{"symbol":"ROTSPY","label":"Test Index","sector":"Index"},{"symbol":"ROTXLK","label":"Test Technology","sector":"Sector"},{"symbol":"ROTXLE","label":"Test Energy","sector":"Sector"}]' "$rotation/internal/rotation/universes/$id/symbols"
snapshot_date=$("${psql[@]}" -Atc "SELECT max(trade_date) FROM market_data_public.adjusted_daily_bars_v1 WHERE symbol IN ('ROTSPY','ROTXLK','ROTXLE')")
curl -fsS -o /dev/null -X POST -H "$auth" "$rotation/internal/rotation/universes/$id/calculate?date=$snapshot_date"
payload=$(curl -fsS -H "$auth" "$rotation/internal/rotation/monitor?universe=ROTATION_SMOKE&date=$snapshot_date")
test "$(jq -r .formulaVersion <<<"$payload")" = rotation-v1
test "$(jq -r .snapshotDate <<<"$payload")" = "$snapshot_date"
test "$(jq -r '.etfs[0].symbol' <<<"$payload")" = ROTXLK
test "$(jq -r .marketState.state <<<"$payload")" = risk_on
test "$(jq '[.etfs[]|select(.status=="ok")]|length' <<<"$payload")" = 3
batch_status=$("${psql[@]}" -Atc "SELECT status FROM rotation.batch_runs WHERE universe_id='$id' AND snapshot_date='$snapshot_date' AND formula_version='rotation-v1'")
test "$batch_status" = completed
source_max_date=$("${psql[@]}" -Atc "SELECT source_max_date FROM rotation.batch_runs WHERE universe_id='$id' AND snapshot_date='$snapshot_date' AND formula_version='rotation-v1'")
test "$source_max_date" = "$snapshot_date"
calculated_at=$("${psql[@]}" -Atc "SELECT min(calculated_at) FROM rotation.market_rotation_snapshots WHERE universe_id='$id' AND snapshot_date='$snapshot_date'")
curl -fsS -o /dev/null -X POST -H "$auth" "$rotation/internal/rotation/universes/$id/calculate?date=$snapshot_date"
test "$("${psql[@]}" -Atc "SELECT min(calculated_at) FROM rotation.market_rotation_snapshots WHERE universe_id='$id' AND snapshot_date='$snapshot_date'")" = "$calculated_at"
echo 'rotation smoke: ok'
