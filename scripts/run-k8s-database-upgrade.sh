#!/usr/bin/env bash
set -euo pipefail

namespace="${K8S_NAMESPACE:-micro-cockpit}"
registry=""
image_tag=""
retry_failed_jobs=false
script_dir=$(CDPATH= cd -- "$(dirname "$0")" && pwd)

usage() { echo "Usage: $0 [--namespace NAME] --image-registry REGISTRY --image-tag COMMIT_SHA [--retry-failed-jobs]" >&2; }
while [ "$#" -gt 0 ]; do
  case "$1" in
    --namespace) namespace=${2:?missing namespace}; shift 2 ;;
    --image-registry) registry=${2:?missing registry}; shift 2 ;;
    --image-tag) image_tag=${2:?missing image tag}; shift 2 ;;
    --retry-failed-jobs) retry_failed_jobs=true; shift ;;
    *) usage; exit 2 ;;
  esac
done

[[ "$namespace" =~ ^[a-z0-9]([-a-z0-9]*[a-z0-9])?$ ]] || { echo "Invalid namespace." >&2; exit 1; }
[[ "$registry" =~ ^[A-Za-z0-9._:/-]+$ ]] || { echo "Invalid image registry." >&2; exit 1; }
[[ "$image_tag" =~ ^[0-9a-f]{40}$ ]] || { echo "Database tooling image tag must be a full lowercase commit SHA." >&2; exit 1; }

tmp=$(mktemp -d "${TMPDIR:-/tmp}/micro-cockpit-db-upgrade.XXXXXX")
cleanup() { rm -rf "$tmp"; }
trap cleanup EXIT HUP INT TERM
chmod 700 "$tmp"
umask 077
image="${registry%/}/db-migrator:${image_tag}"
suffix=${image_tag:0:12}
# shellcheck source=scripts/k8s-database-job-lib.sh
source "$script_dir/k8s-database-job-lib.sh"

kubectl rollout status deployment/postgres -n "$namespace" --timeout=300s
database_job_run "db-bootstrap-$suffix" bootstrap "$retry_failed_jobs"
database_job_run "db-migrate-$suffix" migrate "$retry_failed_jobs"
database_job_run "db-finalize-$suffix" finalize "$retry_failed_jobs"
echo "Database upgrade Jobs completed for release $image_tag."
