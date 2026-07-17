#!/usr/bin/env bash
# Fails if journal/reminder Dockerfiles cannot restore+publish against the shared event contract.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"

fail() { echo "verify-docker-shared-events: $*" >&2; exit 1; }

for dockerfile in \
  services/journal-service/src/TradeDiary.Journal/Dockerfile \
  services/reminder-service/src/TradeDiary.Reminder/Dockerfile
do
  grep -q 'contracts/TradeDiary.Events/TradeDiary.Events.csproj' "$dockerfile" \
    || fail "$dockerfile missing event-contract .csproj COPY before restore"
  grep -q 'contracts/TradeDiary.Events/' "$dockerfile" \
    || fail "$dockerfile missing event-contract source COPY"
done

# Full image builds catch a missing shared-project COPY at restore/publish time.
docker build -f services/journal-service/src/TradeDiary.Journal/Dockerfile .
docker build -f services/reminder-service/src/TradeDiary.Reminder/Dockerfile .

echo "verify-docker-shared-events: ok"
