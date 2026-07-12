#!/usr/bin/env bash
set -euo pipefail

# Builds and starts the declared deployment, then proves every container is healthy.
# KEEP_STACK=1 leaves it running for inspection.
export COMPOSE_PROJECT_NAME="${COMPOSE_PROJECT_NAME:-micro-cockpit-smoke-$$}"
cleanup() { test "${KEEP_STACK:-0}" = 1 || docker compose down --volumes; }
trap cleanup EXIT

docker compose config --quiet
docker compose up -d --build --wait

expected=$(docker compose config --services | grep -v '^db-init$' | sort)
running=$(docker compose ps --status running --services | sort)
test "$running" = "$expected" || { echo "not every declared service is running" >&2; exit 1; }
for container in $(docker compose ps -q); do
  test "$(docker inspect --format '{{.State.Health.Status}}' "$container")" = healthy || {
    echo "unhealthy container: $container" >&2
    exit 1
  }
done
curl -fsS http://127.0.0.1:5099/health/ready >/dev/null
curl -fsS http://127.0.0.1:8080/ >/dev/null
echo "container smoke: all declared containers running; public endpoints ready"
