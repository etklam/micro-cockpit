#!/usr/bin/env bash
set -euo pipefail
set +x

namespace="${K8S_NAMESPACE:-micro-cockpit}"
backup_reference="${BACKUP_CONFIRMED:-}"
expected_context="${K8S_CONTEXT:-}"

usage() {
  echo "Usage: $0 [--namespace NAME] --context CONTEXT --backup-confirmed REFERENCE" >&2
}

while [ "$#" -gt 0 ]; do
  case "$1" in
    --namespace) namespace=${2:?missing namespace}; shift 2 ;;
    --context) expected_context=${2:?missing context}; shift 2 ;;
    --backup-confirmed) backup_reference=${2:?missing backup reference}; shift 2 ;;
    *) usage; exit 2 ;;
  esac
done

[ -n "$expected_context" ] || { echo "An explicit Kubernetes context is required." >&2; exit 1; }
[ -n "$backup_reference" ] || { echo "A verified backup reference is required." >&2; exit 1; }
[[ "$namespace" =~ ^[a-z0-9]([-a-z0-9]*[a-z0-9])?$ ]] || { echo "Invalid namespace." >&2; exit 1; }
for command in kubectl openssl python3 curl; do
  command -v "$command" >/dev/null 2>&1 || { echo "$command is required." >&2; exit 1; }
done

current_context=$(kubectl config current-context)
[ "$current_context" = "$expected_context" ] || { echo "Kubernetes context does not match the operator confirmation." >&2; exit 1; }
kubectl get namespace "$namespace" >/dev/null
kubectl rollout status deployment/postgres -n "$namespace" --timeout=300s

tmp_dir=$(mktemp -d "${TMPDIR:-/tmp}/micro-cockpit-rotation.XXXXXX")
chmod 700 "$tmp_dir"
umask 077
old_file="$tmp_dir/previous.secret.env"
new_file="$tmp_dir/rotated.secret.env"
replicas_file="$tmp_dir/internal-replicas"
port_forward_pid=""
rollback_needed=false

role_mappings=(
  trade_diary:POSTGRES_PASSWORD
  trade_diary_migrator:MIGRATOR_DB_PASSWORD
  identity_service:IDENTITY_DB_PASSWORD
  journal_service:JOURNAL_DB_PASSWORD
  performance_service:PERFORMANCE_DB_PASSWORD
  discipline_service:DISCIPLINE_DB_PASSWORD
  reminder_service:REMINDER_DB_PASSWORD
  stock_research_service:STOCK_RESEARCH_DB_PASSWORD
  market_data_service:MARKET_DATA_DB_PASSWORD
  price_alert_service:PRICE_ALERT_DB_PASSWORD
  rotation_service:ROTATION_DB_PASSWORD
  partner_service:PARTNER_DB_PASSWORD
  content_service:CONTENT_DB_PASSWORD
  operations_service:OPERATIONS_DB_PASSWORD
)
deployments=(identity journal performance discipline reminder stock-research market-data price-alert rotation partner content operations)
internal_key_deployments=(journal reminder market-data price-alert operations)
required_keys=(POSTGRES_PASSWORD MIGRATOR_DB_PASSWORD IDENTITY_DB_PASSWORD JOURNAL_DB_PASSWORD PERFORMANCE_DB_PASSWORD DISCIPLINE_DB_PASSWORD REMINDER_DB_PASSWORD STOCK_RESEARCH_DB_PASSWORD MARKET_DATA_DB_PASSWORD PRICE_ALERT_DB_PASSWORD ROTATION_DB_PASSWORD PARTNER_DB_PASSWORD CONTENT_DB_PASSWORD OPERATIONS_DB_PASSWORD LOCAL_REGISTRATION_KEY INTERNAL_SERVICE_KEY)

secret_value() {
  local file=$1 key=$2
  awk -v key="$key" 'index($0, key "=") == 1 { print substr($0, length(key) + 2); found=1; exit } END { if (!found) exit 1 }' "$file"
}

stop_port_forward() {
  if [ -n "$port_forward_pid" ]; then
    kill "$port_forward_pid" >/dev/null 2>&1 || true
    wait "$port_forward_pid" >/dev/null 2>&1 || true
    port_forward_pid=""
  fi
}

write_role_sql() {
  local env_file=$1 output=$2 mapping role key password encoded
  {
    printf '%s\n' \
      'BEGIN;' \
      'CREATE TEMP TABLE rotation_credentials(role_name text PRIMARY KEY, encoded_password text NOT NULL);' \
      'COPY rotation_credentials(role_name, encoded_password) FROM STDIN;'
    for mapping in "${role_mappings[@]}"; do
      role=${mapping%%:*}
      key=${mapping#*:}
      password=$(secret_value "$env_file" "$key")
      encoded=$(printf '%s' "$password" | python3 -c 'import base64,sys; print(base64.b64encode(sys.stdin.buffer.read()).decode())')
      printf '%s\t%s\n' "$role" "$encoded"
    done
    printf '%s\n' \
      '\.' \
      'DO $$' \
      'BEGIN' \
      "  IF (SELECT count(*) FROM rotation_credentials) <> 14 OR EXISTS (SELECT 1 FROM rotation_credentials WHERE role_name <> ALL (ARRAY['trade_diary','trade_diary_migrator','identity_service','journal_service','performance_service','discipline_service','reminder_service','stock_research_service','market_data_service','price_alert_service','rotation_service','partner_service','content_service','operations_service'])) THEN" \
      "    RAISE EXCEPTION 'Unexpected PostgreSQL role identifier';" \
      '  END IF;' \
      'END' \
      '$$;' \
      "SELECT format('ALTER ROLE %I PASSWORD %L', role_name, convert_from(decode(encoded_password, 'base64'), 'UTF8')) FROM rotation_credentials" \
      '\gexec' \
      'COMMIT;'
  } >"$output"
  chmod 600 "$output"
}

apply_role_passwords() {
  local env_file=$1 sql_file="$tmp_dir/role-passwords.sql"
  write_role_sql "$env_file" "$sql_file"
  kubectl exec -i -n "$namespace" deployment/postgres -- \
    psql -U trade_diary -d trade_diary -v ON_ERROR_STOP=1 <"$sql_file" >/dev/null
}

write_pgpass() {
  local env_file=$1 key=$2 role=$3 output=$4 password
  password=$(secret_value "$env_file" "$key")
  password=${password//\\/\\\\}
  password=${password//:/\\:}
  printf '127.0.0.1:5432:trade_diary:%s:%s\n' "$role" "$password" >"$output"
  chmod 600 "$output"
}

database_login() {
  local env_file=$1 key=$2 role=$3 pgpass="$tmp_dir/login.pgpass"
  write_pgpass "$env_file" "$key" "$role" "$pgpass"
  kubectl exec -i -n "$namespace" deployment/postgres -- sh -c '
    set -eu
    umask 077
    pgpass=$(mktemp)
    cleanup() { rm -f "$pgpass"; }
    trap cleanup EXIT HUP INT TERM
    cat >"$pgpass"
    PGPASSFILE="$pgpass" psql -h 127.0.0.1 -U "$1" -d trade_diary -v ON_ERROR_STOP=1 -Atqc "SELECT 1" >/dev/null 2>&1
  ' sh "$role" <"$pgpass" >/dev/null 2>&1
}

verify_logins_succeed() {
  local env_file=$1 mapping role key
  for mapping in "${role_mappings[@]}"; do
    role=${mapping%%:*}; key=${mapping#*:}
    database_login "$env_file" "$key" "$role" || { echo "Database login verification failed for role: $role" >&2; return 1; }
  done
}

verify_logins_fail() {
  local env_file=$1 mapping role key
  for mapping in "${role_mappings[@]}"; do
    role=${mapping%%:*}; key=${mapping#*:}
    if database_login "$env_file" "$key" "$role"; then
      echo "Previous database credential was still accepted for role: $role" >&2
      return 1
    fi
  done
}

restore_internal_replicas() {
  local deployment replicas
  while read -r deployment replicas; do
    [ -n "$deployment" ] || continue
    kubectl scale deployment/"$deployment" -n "$namespace" --replicas="$replicas" >/dev/null
  done <"$replicas_file"
}

restart_and_wait() {
  local deployment
  restore_internal_replicas
  for deployment in "${deployments[@]}"; do
    kubectl rollout restart deployment/"$deployment" -n "$namespace" >/dev/null
  done
  for deployment in "${deployments[@]}"; do
    kubectl rollout status deployment/"$deployment" -n "$namespace" --timeout=300s
  done
}

start_port_forward() {
  local port
  if [ "${ROTATION_TEST_MODE:-0}" = 1 ] && [ -n "${ROTATION_TEST_PORT:-}" ]; then
    port=$ROTATION_TEST_PORT
  else
    port=$(python3 -c 'import socket; s=socket.socket(); s.bind(("127.0.0.1",0)); print(s.getsockname()[1]); s.close()')
  fi
  printf '%s' "$port" >"$tmp_dir/probe-port"
  kubectl port-forward -n "$namespace" service/market-data "${port}:8080" >"$tmp_dir/port-forward.log" 2>&1 &
  port_forward_pid=$!
  for _ in $(seq 1 30); do
    if curl -fsS "http://127.0.0.1:${port}/health/ready" >/dev/null 2>&1; then return 0; fi
    sleep 1
  done
  echo "Internal-key probe could not reach Market Data readiness." >&2
  return 1
}

probe_internal_key() {
  local env_file=$1 expected=$2 key header_file="$tmp_dir/service-key.header" port status
  key=$(secret_value "$env_file" INTERNAL_SERVICE_KEY)
  printf 'X-Service-Key: %s\n' "$key" >"$header_file"
  chmod 600 "$header_file"
  port=$(<"$tmp_dir/probe-port")
  status=$(curl -sS -o /dev/null -w '%{http_code}' -X PUT \
    -H @"$header_file" -H 'Content-Type: application/json' --data '{}' \
    "http://127.0.0.1:${port}/internal/admin/symbols/-")
  [ "$status" = "$expected" ] || { echo "Internal service-key authorization probe returned an unexpected status." >&2; return 1; }
}

restore_secret_data() {
  local secret
  for secret in db-credentials service-connection-strings app-secrets; do
    kubectl patch secret "$secret" -n "$namespace" --type json \
      --patch-file="$tmp_dir/${secret}.patch.json" >/dev/null
  done
}

rollback_rotation() {
  local failed=0
  echo "Rotation failed after the database change; starting credential recovery." >&2
  stop_port_forward
  apply_role_passwords "$old_file" || failed=1
  restore_secret_data || failed=1
  restart_and_wait || failed=1
  verify_logins_succeed "$old_file" || failed=1
  if [ "$failed" -eq 0 ]; then
    start_port_forward || failed=1
    if [ "$failed" -eq 0 ]; then probe_internal_key "$old_file" 400 || failed=1; fi
    stop_port_forward
  fi
  if [ "$failed" -ne 0 ]; then
    echo "EMERGENCY: automated recovery did not complete. Keep the maintenance window active, restore PostgreSQL role passwords from the protected incident material, restore all three Kubernetes Secrets, restart affected deployments, and verify old runtime logins before reopening traffic." >&2
    return 1
  fi
  echo "Credential recovery completed; the rotation remains unsuccessful." >&2
}

on_exit() {
  local rc=$?
  trap - EXIT HUP INT TERM
  set +e
  stop_port_forward
  if [ "$rc" -ne 0 ] && [ "$rollback_needed" = true ]; then
    rollback_rotation || rc=1
  fi
  rm -rf "$tmp_dir"
  exit "$rc"
}
trap on_exit EXIT
trap 'exit 130' HUP INT TERM

fault() {
  if [ "${ROTATION_TEST_MODE:-0}" = 1 ] && [ "${ROTATION_FAULT_INJECTION:-}" = "$1" ]; then
    echo "Injected rotation test failure at stage: $1" >&2
    return 1
  fi
}

# Capture exact Secret data and a decoded copy of the credentials in protected temporary files.
for secret in db-credentials service-connection-strings app-secrets; do
  kubectl get secret "$secret" -n "$namespace" -o json >"$tmp_dir/${secret}.json"
  chmod 600 "$tmp_dir/${secret}.json"
done
python3 - "$tmp_dir" "$old_file" <<'PY'
import base64, json, pathlib, sys
root, output = pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2])
merged = {}
for name in ("db-credentials", "app-secrets"):
    obj = json.loads((root / f"{name}.json").read_text())
    for key, value in obj.get("data", {}).items():
        decoded = base64.b64decode(value).decode()
        if "\n" in decoded or "\r" in decoded:
            raise SystemExit(f"Secret {key} contains unsupported newline characters")
        merged[key] = decoded
    (root / f"{name}.patch.json").write_text(json.dumps([{"op": "replace", "path": "/data", "value": obj.get("data", {})}]))
connections = json.loads((root / "service-connection-strings.json").read_text())
(root / "service-connection-strings.patch.json").write_text(json.dumps([{"op": "replace", "path": "/data", "value": connections.get("data", {})}]))
with output.open("w") as handle:
    for key, value in merged.items():
        handle.write(f"{key}={value}\n")
PY
chmod 600 "$old_file" "$tmp_dir"/*.patch.json
for key in "${required_keys[@]}"; do secret_value "$old_file" "$key" >/dev/null; done

: >"$replicas_file"
for deployment in "${internal_key_deployments[@]}"; do
  replicas=$(kubectl get deployment "$deployment" -n "$namespace" -o jsonpath='{.spec.replicas}')
  printf '%s %s\n' "$deployment" "${replicas:-1}" >>"$replicas_file"
done
chmod 600 "$replicas_file"

verify_logins_succeed "$old_file"

: >"$new_file"
for key in "${required_keys[@]}"; do
  printf '%s=%s\n' "$key" "$(openssl rand -hex 32)" >>"$new_file"
done
chmod 600 "$new_file"

fault before-db
apply_role_passwords "$new_file"
rollback_needed=true
fault after-db

# Stop every internal-key producer and consumer before changing the shared key.
for deployment in "${internal_key_deployments[@]}"; do
  kubectl scale deployment/"$deployment" -n "$namespace" --replicas=0 >/dev/null
done
for deployment in "${internal_key_deployments[@]}"; do
  kubectl wait --for=delete pod -n "$namespace" -l "app=$deployment" --timeout=300s >/dev/null
done

scripts/provision-k8s-secrets.sh --env-file "$new_file" --namespace "$namespace"
fault after-secrets
restore_internal_replicas
for deployment in "${deployments[@]}"; do
  kubectl rollout restart deployment/"$deployment" -n "$namespace" >/dev/null
done
fault during-rollout
for deployment in "${deployments[@]}"; do
  kubectl rollout status deployment/"$deployment" -n "$namespace" --timeout=300s
done

verify_logins_succeed "$new_file"
verify_logins_fail "$old_file"
start_port_forward
probe_internal_key "$old_file" 403
probe_internal_key "$new_file" 400
stop_port_forward

rollback_needed=false
echo "Credential rotation and positive/negative runtime verification completed."
