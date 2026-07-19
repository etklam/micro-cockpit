#!/usr/bin/env bash
# Disposable-stack smoke: invitation → redeem → share → compare → unshare → revoke.
set -euo pipefail
: "${EDGE_URL:=http://127.0.0.1:5099}"
: "${TEST_PASSWORD:?set TEST_PASSWORD}"

stamp=$(date +%s)
alice_email="alice.partner.${stamp}@example.com"
bob_email="bob.partner.${stamp}@example.com"
name_a="Alice ${stamp}"
name_b="Bob ${stamp}"

register() {
  local email=$1 name=$2
  curl -sS -o /tmp/partner-reg.json -w '%{http_code}' -X POST "$EDGE_URL/api/auth/register" \
    -H 'Content-Type: application/json' \
    -d "{\"email\":\"$email\",\"password\":\"$TEST_PASSWORD\",\"displayName\":\"$name\",\"timezone\":\"UTC\",\"baseCurrency\":\"USD\"}"
}

login() {
  local email=$1
  curl -sS -X POST "$EDGE_URL/api/auth/login" \
    -H 'Content-Type: application/json' \
    -d "{\"email\":\"$email\",\"password\":\"$TEST_PASSWORD\"}" | jq -r .accessToken
}

code=$(register "$alice_email" "$name_a")
test "$code" = 201 -o "$code" = 200
code=$(register "$bob_email" "$name_b")
test "$code" = 201 -o "$code" = 200

alice=$(login "$alice_email")
bob=$(login "$bob_email")
test -n "$alice" -a "$alice" != null
test -n "$bob" -a "$bob" != null
auth_a="Authorization: Bearer $alice"
auth_b="Authorization: Bearer $bob"

# 1) User A creates invitation
invite=$(curl -sS -X POST "$EDGE_URL/api/app/partners/invitations" -H "$auth_a")
code=$(echo "$invite" | jq -r .code)
link_invite_id=$(echo "$invite" | jq -r .id)
test -n "$code" -a "$code" != null

# 2) User B redeems
redeem=$(curl -sS -X POST "$EDGE_URL/api/app/partners/invitations/redeem" -H "$auth_b" -H 'Content-Type: application/json' -d "{\"code\":\"$code\"}")
link_id=$(echo "$redeem" | jq -r .linkId)
test -n "$link_id" -a "$link_id" != null

# Alice writes a diary with a private review field via journal APIs
today=$(date -u +%F)
diary=$(curl -sS -X POST "$EDGE_URL/api/app/diaries" -H "$auth_a" -H 'Content-Type: application/json' -H "Idempotency-Key: partner-smoke-$stamp" \
  -d "{\"localDate\":\"$today\",\"title\":\"Smoke day\",\"content\":\"Shared **body**\",\"tags\":[\"smoke\"]}")
diary_id=$(echo "$diary" | jq -r .id)
curl -sS -o /dev/null -X PUT "$EDGE_URL/api/app/diaries/$diary_id/review" -H "$auth_a" -H 'Content-Type: application/json' \
  -d '{"thesis":"private thesis","plannedAction":null,"actualAction":null,"emotion":"calm","disciplineScore":5,"executionScore":5,"processAssessment":"good","mistakeTags":["no_plan"],"lesson":null,"nextAction":null}'
curl -sS -o /dev/null -X POST "$EDGE_URL/api/app/diaries/$diary_id/transactions" -H "$auth_a" -H 'Content-Type: application/json' -H "Idempotency-Key: partner-tx-$stamp" \
  -d '{"symbol":"AAPL","side":"buy","quantity":1,"price":100,"currency":"USD","tradedAt":"2026-07-10T15:00:00Z","notes":"secret"}'

# 3) User A enables diary sharing
test "$(curl -sS -o /dev/null -w '%{http_code}' -X PUT "$EDGE_URL/api/app/partners/$link_id/share-policy" -H "$auth_a" -H 'Content-Type: application/json' -d '{"shareDiaries":true}')" = 204

# 4) User B opens compare and sees shared diary
compare=$(curl -sS "$EDGE_URL/api/app/partners/$link_id/compare?from=$today&to=$today" -H "$auth_b")
echo "$compare" | jq -e '.capabilities.partnerDiaries == "available"' >/dev/null
echo "$compare" | jq -e --arg t "$today" '.days[] | select(.localDate==$t) | .partner | map(.title) | index("Smoke day") != null' >/dev/null
# 5) No transactions / review fields
echo "$compare" | jq -e 'tostring | contains("private thesis") | not' >/dev/null
echo "$compare" | jq -e 'tostring | contains("AAPL") | not' >/dev/null
echo "$compare" | jq -e 'tostring | contains("disciplineScore") | not' >/dev/null

# 6) Disable sharing → access disappears
test "$(curl -sS -o /dev/null -w '%{http_code}' -X PUT "$EDGE_URL/api/app/partners/$link_id/share-policy" -H "$auth_a" -H 'Content-Type: application/json' -d '{"shareDiaries":false}')" = 204
compare2=$(curl -sS "$EDGE_URL/api/app/partners/$link_id/compare?from=$today&to=$today" -H "$auth_b")
echo "$compare2" | jq -e '.capabilities.partnerDiaries == "not_shared"' >/dev/null
echo "$compare2" | jq -e '[.days[].partner[]] | length == 0' >/dev/null

# 7) Revoke relationship → compare blocked
test "$(curl -sS -o /dev/null -w '%{http_code}' -X DELETE "$EDGE_URL/api/app/partners/$link_id" -H "$auth_a")" = 204
code=$(curl -sS -o /dev/null -w '%{http_code}' "$EDGE_URL/api/app/partners/$link_id/compare?from=$today&to=$today" -H "$auth_b")
test "$code" = 404

echo "smoke-partner-compare: ok (invite=$link_invite_id link=$link_id)"
