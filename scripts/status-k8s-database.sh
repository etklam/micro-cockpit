#!/usr/bin/env bash
set -euo pipefail

namespace=""
context=""
registry=""
image_tag=""
script_dir=$(CDPATH= cd -- "$(dirname "$0")" && pwd)
repo=$(CDPATH= cd -- "$script_dir/.." && pwd)
usage() { echo "Usage: $0 --context CONTEXT --namespace NAME --image-registry REGISTRY --image-tag COMMIT_SHA" >&2; }
while [ "$#" -gt 0 ]; do
  case "$1" in
    --context) context=${2:?missing context}; shift 2 ;;
    --namespace) namespace=${2:?missing namespace}; shift 2 ;;
    --image-registry) registry=${2:?missing registry}; shift 2 ;;
    --image-tag) image_tag=${2:?missing image tag}; shift 2 ;;
    *) usage; exit 2 ;;
  esac
done
[[ "$namespace" =~ ^[a-z0-9]([-a-z0-9]*[a-z0-9])?$ ]] && [ -n "$context" ] || { echo "Context and valid namespace are required." >&2; exit 1; }
[[ "$registry" =~ ^[A-Za-z0-9._:/-]+$ ]] && [[ "$image_tag" =~ ^[0-9a-f]{40}$ ]] || { echo "Invalid immutable database tooling image." >&2; exit 1; }
[ "$(kubectl config current-context)" = "$context" ] || { echo "Kubernetes context does not match operator confirmation." >&2; exit 1; }

tmp=$(mktemp -d "${TMPDIR:-/tmp}/micro-cockpit-db-status.XXXXXX")
cleanup() { rm -rf "$tmp"; }
trap cleanup EXIT HUP INT TERM
chmod 700 "$tmp"
umask 077
image="${registry%/}/db-migrator:${image_tag}"
suffix=${image_tag:0:12}
# shellcheck source=scripts/k8s-database-job-lib.sh
source "$script_dir/k8s-database-job-lib.sh"

history_present=$(kubectl exec deployment/postgres -n "$namespace" -- sh -ceu \
  'export PGPASSWORD="$POSTGRES_PASSWORD"; psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Atc "SELECT to_regclass('"'"'platform_migrations.schema_history'"'"') IS NOT NULL"')
status_code=0
if [ "$history_present" != t ]; then
  managed=$(kubectl exec deployment/postgres -n "$namespace" -- sh -ceu \
    'export PGPASSWORD="$POSTGRES_PASSWORD"; psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Atc "SELECT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname IN ('"'"'identity'"'"','"'"'journal'"'"','"'"'performance'"'"','"'"'discipline'"'"','"'"'reminder'"'"','"'"'market'"'"','"'"'market_data_public'"'"','"'"'price_alert'"'"','"'"'rotation'"'"','"'"'stock_research'"'"','"'"'partner'"'"','"'"'content'"'"','"'"'operations'"'"'))"')
  echo "history-present: false"
  echo "baseline-required: $([ "$managed" = t ] && echo true || echo false)"
  echo "current-migration-id: none"
  echo "pending-ids: $(python3 -c 'import json,sys; print(",".join(x["id"] for x in json.load(open(sys.argv[1]))["migrations"]))' "$repo/platform/postgres/migrations/manifest.json")"
  echo "checksum-mismatch: false"
  echo "missing-applied-file: false"
  echo "out-of-order: false"
  [ "$managed" = t ] && status_code=2
else
  kubectl exec deployment/postgres -n "$namespace" -- sh -ceu \
    'export PGPASSWORD="$POSTGRES_PASSWORD"; psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -At -F "|" -c "SELECT migration_id,filename,checksum_sha256 FROM platform_migrations.schema_history ORDER BY migration_id"' >"$tmp/history"
  python3 "$script_dir/database-migration-status.py" --manifest "$repo/platform/postgres/migrations/manifest.json" --history "$tmp/history" || status_code=$?
fi

for step in bootstrap migrate finalize; do
  base="db-$step-$suffix"
  while IFS= read -r name; do
    [ -n "$name" ] || continue
    database_job_verify "$name" "$step" || exit 1
    complete=$(database_job_condition "$name" Complete)
    failed=$(database_job_condition "$name" Failed)
    [ "$failed" = True ] && echo "database-job-attempt: $step $name failed"
    [ "$failed" != True ] && [ "$complete" = True ] && echo "database-job-attempt: $step $name completed"
  done <<<"$(database_job_list "$base")"
done
exit "$status_code"
