#!/usr/bin/env bash
set -euo pipefail

namespace=""
context=""
registry=""
image_tag=""
backup=""
confirmed=false
retry_failed_job=false
script_dir=$(CDPATH= cd -- "$(dirname "$0")" && pwd)

usage() { echo "Usage: $0 --context CONTEXT --namespace NAME --image-registry REGISTRY --image-tag COMMIT_SHA --backup-confirmed REFERENCE --confirm-existing-database [--retry-failed-job]" >&2; }
while [ "$#" -gt 0 ]; do
  case "$1" in
    --context) context=${2:?missing context}; shift 2 ;;
    --namespace) namespace=${2:?missing namespace}; shift 2 ;;
    --image-registry) registry=${2:?missing registry}; shift 2 ;;
    --image-tag) image_tag=${2:?missing image tag}; shift 2 ;;
    --backup-confirmed) backup=${2:?missing backup reference}; shift 2 ;;
    --confirm-existing-database) confirmed=true; shift ;;
    --retry-failed-job) retry_failed_job=true; shift ;;
    *) usage; exit 2 ;;
  esac
done
[ "$confirmed" = true ] && [ -n "$backup" ] && [ -n "$context" ] && [ -n "$namespace" ] || { echo "Context, namespace, existing-database confirmation, and backup confirmation are required." >&2; exit 1; }
[[ "$namespace" =~ ^[a-z0-9]([-a-z0-9]*[a-z0-9])?$ ]] || { echo "Invalid namespace." >&2; exit 1; }
[[ "$backup" =~ ^[A-Za-z0-9._:/-]+$ ]] || { echo "Backup reference contains unsupported characters." >&2; exit 1; }
[[ "$registry" =~ ^[A-Za-z0-9._:/-]+$ ]] && [[ "$image_tag" =~ ^[0-9a-f]{40}$ ]] || { echo "Invalid immutable database tooling image." >&2; exit 1; }
[ "$(kubectl config current-context)" = "$context" ] || { echo "Kubernetes context does not match operator confirmation." >&2; exit 1; }

retry_args=()
[ "$retry_failed_job" = true ] && retry_args+=(--retry-failed-job)
if [ "${MICRO_COCKPIT_OPERATION_LOCK_NAMESPACE:-}" != "$namespace" ]; then
  exec "$script_dir/with-k8s-operation-lock.sh" --namespace "$namespace" --timeout 900 -- \
    "$0" --context "$context" --namespace "$namespace" --image-registry "$registry" --image-tag "$image_tag" \
      --backup-confirmed "$backup" --confirm-existing-database "${retry_args[@]}"
fi

suffix=${image_tag:0:12}
tmp=$(mktemp -d "${TMPDIR:-/tmp}/micro-cockpit-db-baseline.XXXXXX")
cleanup() { rm -rf "$tmp"; }
trap cleanup EXIT HUP INT TERM
chmod 700 "$tmp"
umask 077
image="${registry%/}/db-migrator:${image_tag}"
# shellcheck source=scripts/k8s-database-job-lib.sh
source "$script_dir/k8s-database-job-lib.sh"
database_job_run "db-baseline-$suffix" baseline "$retry_failed_job" "$backup"
echo "Existing database baseline Job completed with verified specification."
