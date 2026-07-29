#!/usr/bin/env bash
# Fails if the Journal Dockerfile cannot restore+publish against the shared event contract.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"

fail() { echo "verify-docker-shared-events: $*" >&2; exit 1; }

for dockerfile in \
  services/journal-service/src/TradeDiary.Journal/Dockerfile
do
  grep -q 'contracts/TradeDiary.Events/TradeDiary.Events.csproj' "$dockerfile" \
    || fail "$dockerfile missing event-contract .csproj COPY before restore"
  grep -q 'contracts/TradeDiary.Events/' "$dockerfile" \
    || fail "$dockerfile missing event-contract source COPY"
done

# Full image builds catch a missing shared-project COPY at restore/publish time.
docker build -f services/journal-service/src/TradeDiary.Journal/Dockerfile .

echo "verify-docker-shared-events: ok"
