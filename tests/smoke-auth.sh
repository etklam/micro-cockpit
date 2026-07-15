#!/usr/bin/env bash
set -euo pipefail

edge=${EDGE_URL:-http://127.0.0.1:5099}
email=${TEST_EMAIL:-owner@example.com}
password=${TEST_PASSWORD:?set TEST_PASSWORD}
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

login() {
  jar=$1
  curl -sS -H 'Content-Type: application/json' \
    -c "$jar" \
    -d "$(jq -nc --arg email "$email" --arg password "$password" '{email:$email,password:$password}')" "$edge/api/auth/login"
}
status() {
  curl -sS -o /dev/null -w '%{http_code}' "$@"
}
cookie() {
  awk '$6 == "td_refresh" { print $7 }' "$1"
}

first_jar="$tmp/first.cookies"
first=$(login "$first_jar")
access=$(jq -er .accessToken <<<"$first")
test "$(jq -r '.refreshToken // ""' <<<"$first")" = ""
refresh1=$(cookie "$first_jar")
test -n "$refresh1"
test "$(status -H "Authorization: Bearer $access" "$edge/api/app/diaries")" = 200

second=$(curl -sS -H 'Content-Type: application/json' -b "td_refresh=$refresh1" -c "$tmp/second.cookies" -X POST "$edge/api/auth/refresh")
refresh2=$(cookie "$tmp/second.cookies")
test -n "$refresh2"
test "$(status -H 'Content-Type: application/json' -b "td_refresh=$refresh1" -X POST "$edge/api/auth/refresh")" = 401
test "$(status -H 'Content-Type: application/json' -b "td_refresh=$refresh2" -X POST "$edge/api/auth/refresh")" = 401

third_jar="$tmp/third.cookies"
third=$(login "$third_jar")
refresh3=$(cookie "$third_jar")
test -n "$refresh3"
test "$(status -H 'Content-Type: application/json' -b "td_refresh=$refresh3" -c "$tmp/logout.cookies" -X POST "$edge/api/auth/logout")" = 204
test "$(status -H 'Content-Type: application/json' -b "td_refresh=$refresh3" -X POST "$edge/api/auth/refresh")" = 401

echo 'auth smoke: ok'
