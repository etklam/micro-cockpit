#!/usr/bin/env bash
# Disposable-stack smoke: invitation → redeem → diary/review/tx → share → compare → unshare → revoke.
# Requires gated registration (LOCAL_REGISTRATION_KEY) against a disposable Edge stack.
set -euo pipefail
: "${EDGE_URL:=http://127.0.0.1:5099}"
: "${TEST_PASSWORD:?set TEST_PASSWORD}"
: "${LOCAL_REGISTRATION_KEY:?set LOCAL_REGISTRATION_KEY}"

tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

stamp=$(date +%s)
alice_email="alice.partner.${stamp}@example.com"
bob_email="bob.partner.${stamp}@example.com"
name_a="Alice ${stamp}"
name_b="Bob ${stamp}"

# Portable UTC calendar dates (GNU date or BSD date).
utc_today() { date -u +%F; }
utc_days_ago() {
  local n=$1
  if date -u -d '1970-01-01' +%F >/dev/null 2>&1; then
    date -u -d "$n days ago" +%F
  else
    date -u -v-"${n}"d +%F
  fi
}

# Write response body to $tmp/$out.body; print HTTP status on stdout.
# Optional: JSON body as 5th arg. Optional Idempotency-Key as 6th arg.
http() {
  local method=$1 url=$2 auth=$3 out=$4
  local body_file="$tmp/${out}.body"
  local data=${5-}
  local idem=${6-}
  local -a args
  args=(-sS -o "$body_file" -w '%{http_code}' -X "$method" "$url" -H "$auth")
  if [[ -n "$idem" ]]; then
    args+=(-H "Idempotency-Key: $idem")
  fi
  if [[ -n "$data" ]]; then
    args+=(-H 'Content-Type: application/json' -d "$data")
  fi
  curl "${args[@]}"
}

register() {
  local email=$1 name=$2 out=$3
  local code
  code=$(curl -sS -o "$tmp/${out}.body" -w '%{http_code}' -X POST "$EDGE_URL/api/auth/register" \
    -H "X-Registration-Key: $LOCAL_REGISTRATION_KEY" \
    -H 'Content-Type: application/json' \
    -d "$(jq -nc --arg email "$email" --arg password "$TEST_PASSWORD" --arg name "$name" \
      '{email:$email,password:$password,displayName:$name,timezone:"UTC",baseCurrency:"USD"}')")
  test "$code" = 201 -o "$code" = 409
}

login() {
  local email=$1 out=$2
  local code
  code=$(curl -sS -o "$tmp/${out}.body" -w '%{http_code}' -X POST "$EDGE_URL/api/auth/login" \
    -H 'Content-Type: application/json' \
    -d "$(jq -nc --arg email "$email" --arg password "$TEST_PASSWORD" '{email:$email,password:$password}')")
  test "$code" = 200
  jq -er .accessToken "$tmp/${out}.body"
}

register "$alice_email" "$name_a" reg-a
register "$bob_email" "$name_b" reg-b

alice=$(login "$alice_email" login-a)
bob=$(login "$bob_email" login-b)
auth_a="Authorization: Bearer $alice"
auth_b="Authorization: Bearer $bob"

# zh-Hant practicality: account locale persists as a valid settings value.
code=$(http PUT "$EDGE_URL/api/app/settings" "$auth_a" settings-a \
  "$(jq -nc --arg name "$name_a" \
    '{displayName:$name,timezone:"UTC",baseCurrency:"USD",appearance:"system",locale:"zh-Hant"}')")
test "$code" = 200
jq -e '.locale == "zh-Hant"' "$tmp/settings-a.body" >/dev/null

# 1) User A creates invitation (raw code once).
code=$(http POST "$EDGE_URL/api/app/partners/invitations" "$auth_a" invite)
test "$code" = 201
invite_code=$(jq -er .code "$tmp/invite.body")
link_invite_id=$(jq -er .id "$tmp/invite.body")

# 2) User B redeems → accepted human link (invitation path has no separate accept).
code=$(http POST "$EDGE_URL/api/app/partners/invitations/redeem" "$auth_b" redeem \
  "$(jq -nc --arg code "$invite_code" '{code:$code}')")
test "$code" = 200
link_id=$(jq -er .linkId "$tmp/redeem.body")

# Redeemed link is accepted; share policies default off; display name from Identity.
code=$(http GET "$EDGE_URL/api/app/partners/$link_id/summary" "$auth_a" summary-a)
test "$code" = 200
jq -e '.status == "accepted" and .myShareDiaries == false and .partnerShareDiaries == false' \
  "$tmp/summary-a.body" >/dev/null
jq -e --arg n "$name_b" '.partnerDisplayName == $n' "$tmp/summary-a.body" >/dev/null

# Alice writes diary + private review + transaction (must not leak via compare).
today=$(utc_today)
code=$(http POST "$EDGE_URL/api/app/diaries" "$auth_a" diary \
  "$(jq -nc --arg d "$today" '{localDate:$d,title:"Smoke day",content:"Shared **body**",tags:["smoke"]}')" \
  "partner-smoke-$stamp")
test "$code" = 201 -o "$code" = 200
diary_id=$(jq -er .id "$tmp/diary.body")

code=$(http PUT "$EDGE_URL/api/app/diaries/$diary_id/review" "$auth_a" review \
  '{"thesis":"private thesis","plannedAction":null,"actualAction":null,"emotion":"calm","disciplineScore":5,"executionScore":5,"processAssessment":"good","mistakeTags":["no_plan"],"lesson":null,"nextAction":null}')
test "$code" = 200 -o "$code" = 204

code=$(http POST "$EDGE_URL/api/app/diaries/$diary_id/transactions" "$auth_a" tx \
  '{"symbol":"AAPL","side":"buy","quantity":1,"price":100,"currency":"USD","tradedAt":"2026-07-10T15:00:00Z","notes":"secret"}' \
  "partner-tx-$stamp")
test "$code" = 201 -o "$code" = 200

# 3) User A enables diary sharing (independent per side).
code=$(http PUT "$EDGE_URL/api/app/partners/$link_id/share-policy" "$auth_a" share-on '{"shareDiaries":true}')
test "$code" = 204

# 4) User B opens compare and sees shared diary projection only.
code=$(http GET "$EDGE_URL/api/app/partners/$link_id/compare?from=$today&to=$today" "$auth_b" compare)
test "$code" = 200
jq -e '.capabilities.partnerDiaries == "available"' "$tmp/compare.body" >/dev/null
jq -e --arg t "$today" \
  '.days[] | select(.localDate==$t) | .partner | map(.title) | index("Smoke day") != null' \
  "$tmp/compare.body" >/dev/null
jq -e --arg t "$today" \
  '.days[] | select(.localDate==$t) | .partner | map(.content) | index("Shared **body**") != null' \
  "$tmp/compare.body" >/dev/null

# 5) Privacy exclusions: no review fields, transactions, or private notes.
jq -e 'tostring | contains("private thesis") | not' "$tmp/compare.body" >/dev/null
jq -e 'tostring | contains("AAPL") | not' "$tmp/compare.body" >/dev/null
jq -e 'tostring | contains("disciplineScore") | not' "$tmp/compare.body" >/dev/null
jq -e 'tostring | contains("secret") | not' "$tmp/compare.body" >/dev/null
jq -e 'tostring | contains("thesis") | not' "$tmp/compare.body" >/dev/null
jq -e 'tostring | contains("executionScore") | not' "$tmp/compare.body" >/dev/null
jq -e 'tostring | contains("mistakeTags") | not' "$tmp/compare.body" >/dev/null

# Inclusive 366-day window: DayNumber(to)-DayNumber(from) <= 365 accepted; larger is 400.
from_ok=$(utc_days_ago 365)
code=$(http GET "$EDGE_URL/api/app/partners/$link_id/compare?from=$from_ok&to=$today" "$auth_b" compare-ok-range)
test "$code" = 200
from_bad=$(utc_days_ago 366)
code=$(http GET "$EDGE_URL/api/app/partners/$link_id/compare?from=$from_bad&to=$today" "$auth_b" compare-bad-range)
test "$code" = 400

# 6) Disable sharing → access disappears.
code=$(http PUT "$EDGE_URL/api/app/partners/$link_id/share-policy" "$auth_a" share-off '{"shareDiaries":false}')
test "$code" = 204
code=$(http GET "$EDGE_URL/api/app/partners/$link_id/compare?from=$today&to=$today" "$auth_b" compare2)
test "$code" = 200
jq -e '.capabilities.partnerDiaries == "not_shared"' "$tmp/compare2.body" >/dev/null
jq -e '[.days[].partner[]] | length == 0' "$tmp/compare2.body" >/dev/null

# 7) Revoke relationship → compare blocked as non-disclosing 404.
code=$(http DELETE "$EDGE_URL/api/app/partners/$link_id" "$auth_a" revoke)
test "$code" = 204
code=$(http GET "$EDGE_URL/api/app/partners/$link_id/compare?from=$today&to=$today" "$auth_b" compare3)
test "$code" = 404

echo "smoke-partner-compare: ok (invite=$link_invite_id link=$link_id)"
