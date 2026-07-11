#!/usr/bin/env bash
set -euo pipefail

base=http://127.0.0.1:5100
login() {
  curl -sS -H 'Content-Type: application/json' \
    -d '{"email":"owner@example.com","password":"correct-horse-battery-staple"}' "$base/internal/auth/login"
}
status() {
  curl -sS -o /dev/null -w '%{http_code}' "$@"
}

first=$(login)
access=$(jq -r .accessToken <<<"$first")
refresh1=$(jq -r .refreshToken <<<"$first")
test "$(status -H "Authorization: Bearer $access" "$base/internal/auth/me")" = 200

second=$(curl -sS -H 'Content-Type: application/json' -d "{\"refreshToken\":\"$refresh1\"}" "$base/internal/auth/refresh")
refresh2=$(jq -r .refreshToken <<<"$second")
test "$(status -H 'Content-Type: application/json' -d "{\"refreshToken\":\"$refresh1\"}" "$base/internal/auth/refresh")" = 401
test "$(status -H 'Content-Type: application/json' -d "{\"refreshToken\":\"$refresh2\"}" "$base/internal/auth/refresh")" = 401

third=$(login)
refresh3=$(jq -r .refreshToken <<<"$third")
test "$(status -H 'Content-Type: application/json' -d "{\"refreshToken\":\"$refresh3\"}" "$base/internal/auth/logout")" = 204
test "$(status -H 'Content-Type: application/json' -d "{\"refreshToken\":\"$refresh3\"}" "$base/internal/auth/refresh")" = 401

echo 'auth smoke: ok'
