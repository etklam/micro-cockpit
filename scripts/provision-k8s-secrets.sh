#!/usr/bin/env bash
set -euo pipefail
set +x

namespace="${K8S_NAMESPACE:-micro-cockpit}"
source_file="${SECRET_ENV_FILE:-}"

usage() {
  echo "Usage: $0 --env-file PATH [--namespace NAME]" >&2
}

while [ "$#" -gt 0 ]; do
  case "$1" in
    --env-file) source_file=${2:?missing env-file path}; shift 2 ;;
    --namespace) namespace=${2:?missing namespace}; shift 2 ;;
    *) usage; exit 2 ;;
  esac
done

[ -n "$source_file" ] || { usage; exit 2; }
[ -r "$source_file" ] || { echo "Secret input file is not readable." >&2; exit 1; }
command -v kubectl >/dev/null 2>&1 || { echo "kubectl is required." >&2; exit 1; }

tmp_dir=$(mktemp -d "${TMPDIR:-/tmp}/micro-cockpit-secrets.XXXXXX")
cleanup() {
  rm -rf "$tmp_dir"
}
trap cleanup EXIT HUP INT TERM
chmod 700 "$tmp_dir"
umask 077

required='POSTGRES_PASSWORD MIGRATOR_DB_PASSWORD IDENTITY_DB_PASSWORD JOURNAL_DB_PASSWORD PERFORMANCE_DB_PASSWORD DISCIPLINE_DB_PASSWORD REMINDER_DB_PASSWORD STOCK_RESEARCH_DB_PASSWORD MARKET_DATA_DB_PASSWORD PRICE_ALERT_DB_PASSWORD ROTATION_DB_PASSWORD PARTNER_DB_PASSWORD CONTENT_DB_PASSWORD OPERATIONS_DB_PASSWORD LOCAL_REGISTRATION_KEY INTERNAL_SERVICE_KEY'

read_value() {
  key=$1
  value=$(awk -v key="$key" 'index($0, key "=") == 1 { print substr($0, length(key) + 2); found=1; exit } END { if (!found) exit 1 }' "$source_file") || {
    echo "Missing required secret variable: $key" >&2
    exit 1
  }
  case "$value" in
    ''|REQUIRED|'[REDACTED]') echo "Secret variable is empty or a placeholder: $key" >&2; exit 1 ;;
    *';'*|*'\r'*|*'\n'*) echo "Secret variable contains unsupported characters: $key" >&2; exit 1 ;;
  esac
  printf '%s' "$value"
}

db_file="$tmp_dir/db.secret.env"
app_file="$tmp_dir/app.secret.env"
connections_file="$tmp_dir/connections.secret.env"
: >"$db_file"
: >"$app_file"
: >"$connections_file"
chmod 600 "$db_file" "$app_file" "$connections_file"

for key in $required; do
  value=$(read_value "$key")
  case "$key" in
    LOCAL_REGISTRATION_KEY|INTERNAL_SERVICE_KEY)
      printf '%s=%s\n' "$key" "$value" >>"$app_file"
      ;;
    *)
      printf '%s=%s\n' "$key" "$value" >>"$db_file"
      ;;
  esac
done

for mapping in \
  IDENTITY:identity_service:IDENTITY_DB_PASSWORD \
  JOURNAL:journal_service:JOURNAL_DB_PASSWORD \
  PERFORMANCE:performance_service:PERFORMANCE_DB_PASSWORD \
  DISCIPLINE:discipline_service:DISCIPLINE_DB_PASSWORD \
  REMINDER:reminder_service:REMINDER_DB_PASSWORD \
  STOCK_RESEARCH:stock_research_service:STOCK_RESEARCH_DB_PASSWORD \
  MARKET_DATA:market_data_service:MARKET_DATA_DB_PASSWORD \
  PRICE_ALERT:price_alert_service:PRICE_ALERT_DB_PASSWORD \
  ROTATION:rotation_service:ROTATION_DB_PASSWORD \
  PARTNER:partner_service:PARTNER_DB_PASSWORD \
  CONTENT:content_service:CONTENT_DB_PASSWORD \
  OPERATIONS:operations_service:OPERATIONS_DB_PASSWORD
do
  prefix=${mapping%%:*}
  rest=${mapping#*:}
  role=${rest%%:*}
  password_key=${rest#*:}
  password=$(read_value "$password_key")
  printf '%s_CONNECTION_STRING=Host=postgres;Database=trade_diary;Username=%s;Password=%s\n' \
    "$prefix" "$role" "$password" >>"$connections_file"
done

kubectl get namespace "$namespace" >/dev/null
for item in db-credentials:"$db_file" service-connection-strings:"$connections_file" app-secrets:"$app_file"; do
  name=${item%%:*}
  file=${item#*:}
  kubectl create secret generic "$name" --namespace "$namespace" \
    --from-env-file="$file" --dry-run=client -o yaml |
    kubectl apply --namespace "$namespace" -f - >/dev/null
done

echo "Kubernetes secrets were provisioned in namespace $namespace."
