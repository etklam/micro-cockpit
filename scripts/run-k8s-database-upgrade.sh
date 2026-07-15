#!/usr/bin/env bash
set -euo pipefail

namespace="${K8S_NAMESPACE:-micro-cockpit}"
registry=""
image_tag=""

usage() { echo "Usage: $0 [--namespace NAME] --image-registry REGISTRY --image-tag COMMIT_SHA" >&2; }
while [ "$#" -gt 0 ]; do
  case "$1" in
    --namespace) namespace=${2:?missing namespace}; shift 2 ;;
    --image-registry) registry=${2:?missing registry}; shift 2 ;;
    --image-tag) image_tag=${2:?missing image tag}; shift 2 ;;
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

admin_env() {
  cat <<'YAML'
            - name: PGHOST
              value: postgres
            - name: PGDATABASE
              value: trade_diary
            - name: PGUSER
              value: trade_diary
            - name: PGPASSWORD
              valueFrom: { secretKeyRef: { name: db-credentials, key: POSTGRES_PASSWORD } }
YAML
}

password_env() {
  local key
  for key in MIGRATOR_DB_PASSWORD IDENTITY_DB_PASSWORD JOURNAL_DB_PASSWORD PERFORMANCE_DB_PASSWORD DISCIPLINE_DB_PASSWORD REMINDER_DB_PASSWORD MARKET_DATA_DB_PASSWORD PRICE_ALERT_DB_PASSWORD ROTATION_DB_PASSWORD STOCK_RESEARCH_DB_PASSWORD PARTNER_DB_PASSWORD CONTENT_DB_PASSWORD OPERATIONS_DB_PASSWORD; do
    printf '            - name: %s\n              valueFrom: { secretKeyRef: { name: db-credentials, key: %s } }\n' "$key" "$key"
  done
}

write_job() {
  local name=$1 mode=$2 file="$tmp/$1.yaml"
  {
    printf '%s\n' 'apiVersion: batch/v1' 'kind: Job' 'metadata:' "  name: $name" "  namespace: $namespace" 'spec:' '  backoffLimit: 0' '  template:' '    metadata:' "      labels: { app: $name }" '    spec:' '      restartPolicy: Never' '      containers:' '        - name: database-tooling' "          image: $image" '          imagePullPolicy: Always'
    case "$mode" in
      bootstrap)
        printf '%s\n' '          command: ["/roles/apply.sh"]' '          args: ["bootstrap"]' '          env:'
        admin_env
        password_env
        ;;
      migrate)
        printf '%s\n' '          args: ["migrate"]' '          env:' '            - name: PGHOST' '              value: postgres' '            - name: PGDATABASE' '              value: trade_diary' '            - name: PGUSER' '              value: trade_diary_migrator' '            - name: PGPASSWORD' '              valueFrom: { secretKeyRef: { name: db-credentials, key: MIGRATOR_DB_PASSWORD } }' '            - name: RELEASE_SHA' "              value: \"$image_tag\""
        ;;
      finalize)
        printf '%s\n' '          command: ["/roles/apply.sh"]' '          args: ["finalize"]' '          env:'
        admin_env
        ;;
    esac
  } >"$file"
  chmod 600 "$file"
}

run_job() {
  local name=$1 mode=$2
  write_job "$name" "$mode"
  if kubectl get job "$name" -n "$namespace" >/dev/null 2>&1; then
    complete=$(kubectl get job "$name" -n "$namespace" -o jsonpath='{.status.conditions[?(@.type=="Complete")].status}')
    [ "$complete" = True ] && { echo "Database Job already completed: $name"; return; }
    failed=$(kubectl get job "$name" -n "$namespace" -o jsonpath='{.status.conditions[?(@.type=="Failed")].status}')
    if [ "$failed" = True ]; then
      kubectl get job "$name" -n "$namespace" -o wide >&2
      echo "Database Job previously failed and was preserved: $name" >&2
      return 1
    fi
  else
    kubectl apply -f "$tmp/$name.yaml" >/dev/null
  fi
  if ! kubectl wait --for=condition=Complete job/"$name" -n "$namespace" --timeout=600s; then
    kubectl get job "$name" -n "$namespace" -o wide >&2 || true
    kubectl get pods -n "$namespace" -l "job-name=$name" -o wide >&2 || true
    echo "Database Job did not complete successfully: $name" >&2
    return 1
  fi
}

kubectl rollout status deployment/postgres -n "$namespace" --timeout=300s
run_job "db-bootstrap-$suffix" bootstrap
run_job "db-migrate-$suffix" migrate
run_job "db-finalize-$suffix" finalize
echo "Database upgrade Jobs completed for release $image_tag."
