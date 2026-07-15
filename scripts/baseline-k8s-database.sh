#!/usr/bin/env bash
set -euo pipefail

namespace=""
context=""
registry=""
image_tag=""
backup=""
confirmed=false
script_dir=$(CDPATH= cd -- "$(dirname "$0")" && pwd)

usage() { echo "Usage: $0 --context CONTEXT --namespace NAME --image-registry REGISTRY --image-tag COMMIT_SHA --backup-confirmed REFERENCE --confirm-existing-database" >&2; }
while [ "$#" -gt 0 ]; do
  case "$1" in
    --context) context=${2:?missing context}; shift 2 ;;
    --namespace) namespace=${2:?missing namespace}; shift 2 ;;
    --image-registry) registry=${2:?missing registry}; shift 2 ;;
    --image-tag) image_tag=${2:?missing image tag}; shift 2 ;;
    --backup-confirmed) backup=${2:?missing backup reference}; shift 2 ;;
    --confirm-existing-database) confirmed=true; shift ;;
    *) usage; exit 2 ;;
  esac
done
[ "$confirmed" = true ] && [ -n "$backup" ] || { echo "Explicit existing-database and backup confirmation are required." >&2; exit 1; }
[[ "$namespace" =~ ^[a-z0-9]([-a-z0-9]*[a-z0-9])?$ ]] || { echo "Invalid namespace." >&2; exit 1; }
[[ "$backup" =~ ^[A-Za-z0-9._:/-]+$ ]] || { echo "Backup reference contains unsupported characters." >&2; exit 1; }
[[ "$registry" =~ ^[A-Za-z0-9._:/-]+$ ]] && [[ "$image_tag" =~ ^[0-9a-f]{40}$ ]] || { echo "Invalid immutable database tooling image." >&2; exit 1; }
[ "$(kubectl config current-context)" = "$context" ] || { echo "Kubernetes context does not match operator confirmation." >&2; exit 1; }

if [ "${MICRO_COCKPIT_OPERATION_LOCK_NAMESPACE:-}" != "$namespace" ]; then
  exec "$script_dir/with-k8s-operation-lock.sh" --namespace "$namespace" --timeout 900 -- \
    "$0" --context "$context" --namespace "$namespace" --image-registry "$registry" --image-tag "$image_tag" \
      --backup-confirmed "$backup" --confirm-existing-database
fi

suffix=${image_tag:0:12}
job="db-baseline-$suffix"
tmp=$(mktemp -d "${TMPDIR:-/tmp}/micro-cockpit-db-baseline.XXXXXX")
cleanup() { rm -rf "$tmp"; }
trap cleanup EXIT HUP INT TERM
chmod 700 "$tmp"
umask 077
cat >"$tmp/job.yaml" <<YAML
apiVersion: batch/v1
kind: Job
metadata: { name: $job, namespace: $namespace }
spec:
  backoffLimit: 0
  template:
    metadata: { labels: { app: $job } }
    spec:
      restartPolicy: Never
      containers:
        - name: database-baseline
          image: ${registry%/}/db-migrator:$image_tag
          imagePullPolicy: Always
          args: ["baseline", "--confirm-existing-database", "--backup-confirmed", "$backup"]
          env:
            - { name: PGHOST, value: postgres }
            - { name: PGDATABASE, value: trade_diary }
            - { name: PGUSER, value: trade_diary_migrator }
            - name: PGPASSWORD
              valueFrom: { secretKeyRef: { name: db-credentials, key: MIGRATOR_DB_PASSWORD } }
            - { name: RELEASE_SHA, value: "$image_tag" }
YAML
chmod 600 "$tmp/job.yaml"
kubectl apply -f "$tmp/job.yaml" >/dev/null
kubectl wait --for=condition=Complete job/"$job" -n "$namespace" --timeout=600s
echo "Existing database baseline Job completed."
