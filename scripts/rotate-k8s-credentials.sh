#!/usr/bin/env bash
set -euo pipefail
set +x

namespace="${K8S_NAMESPACE:-micro-cockpit}"
backup_confirmed="${BACKUP_CONFIRMED:-}"
while [ "$#" -gt 0 ]; do
  case "$1" in
    --namespace) namespace=${2:?missing namespace}; shift 2 ;;
    --backup-confirmed) backup_confirmed=${2:?missing backup reference}; shift 2 ;;
    *) echo "Usage: $0 [--namespace NAME] --backup-confirmed REFERENCE" >&2; exit 2 ;;
  esac
done

[ -n "$backup_confirmed" ] || {
  echo "Refusing rotation without --backup-confirmed and an operator-provided backup reference." >&2
  exit 1
}
command -v kubectl >/dev/null 2>&1 || { echo "kubectl is required." >&2; exit 1; }
command -v openssl >/dev/null 2>&1 || { echo "openssl is required." >&2; exit 1; }
kubectl config current-context >/dev/null
kubectl get namespace "$namespace" >/dev/null
kubectl rollout status deployment/postgres -n "$namespace" --timeout=300s

tmp_dir=$(mktemp -d "${TMPDIR:-/tmp}/micro-cockpit-rotation.XXXXXX")
cleanup() { rm -rf "$tmp_dir"; }
trap cleanup EXIT HUP INT TERM
chmod 700 "$tmp_dir"
umask 077
new_file="$tmp_dir/rotated.secret.env"
: >"$new_file"
chmod 600 "$new_file"

keys='POSTGRES_PASSWORD MIGRATOR_DB_PASSWORD IDENTITY_DB_PASSWORD JOURNAL_DB_PASSWORD PERFORMANCE_DB_PASSWORD DISCIPLINE_DB_PASSWORD REMINDER_DB_PASSWORD STOCK_RESEARCH_DB_PASSWORD MARKET_DATA_DB_PASSWORD PRICE_ALERT_DB_PASSWORD ROTATION_DB_PASSWORD PARTNER_DB_PASSWORD CONTENT_DB_PASSWORD OPERATIONS_DB_PASSWORD LOCAL_REGISTRATION_KEY INTERNAL_SERVICE_KEY'
for key in $keys; do
  printf '%s=%s\n' "$key" "$(openssl rand -hex 32)" >>"$new_file"
done

value() {
  awk -v key="$1" 'index($0, key "=") == 1 { print substr($0, length(key) + 2); exit }' "$new_file"
}

# psql's :'variable' form performs SQL literal quoting. Role identifiers are fixed below.
kubectl exec -i -n "$namespace" deployment/postgres -- psql -U trade_diary -d trade_diary \
  -v ON_ERROR_STOP=1 \
  -v admin_password="$(value POSTGRES_PASSWORD)" \
  -v migrator_password="$(value MIGRATOR_DB_PASSWORD)" \
  -v identity_password="$(value IDENTITY_DB_PASSWORD)" \
  -v journal_password="$(value JOURNAL_DB_PASSWORD)" \
  -v performance_password="$(value PERFORMANCE_DB_PASSWORD)" \
  -v discipline_password="$(value DISCIPLINE_DB_PASSWORD)" \
  -v reminder_password="$(value REMINDER_DB_PASSWORD)" \
  -v stock_research_password="$(value STOCK_RESEARCH_DB_PASSWORD)" \
  -v market_data_password="$(value MARKET_DATA_DB_PASSWORD)" \
  -v price_alert_password="$(value PRICE_ALERT_DB_PASSWORD)" \
  -v rotation_password="$(value ROTATION_DB_PASSWORD)" \
  -v partner_password="$(value PARTNER_DB_PASSWORD)" \
  -v content_password="$(value CONTENT_DB_PASSWORD)" \
  -v operations_password="$(value OPERATIONS_DB_PASSWORD)" <<'SQL'
BEGIN;
CREATE TEMP TABLE previous_role_verifiers AS
SELECT rolname, rolpassword FROM pg_authid
WHERE rolname IN ('trade_diary', 'trade_diary_migrator', 'identity_service',
  'journal_service', 'performance_service', 'discipline_service', 'reminder_service',
  'stock_research_service', 'market_data_service', 'price_alert_service',
  'rotation_service', 'partner_service', 'content_service', 'operations_service');
SELECT format('ALTER ROLE %I PASSWORD %L', role_name, new_password)
FROM (VALUES
  ('trade_diary', :'admin_password'),
  ('trade_diary_migrator', :'migrator_password'),
  ('identity_service', :'identity_password'),
  ('journal_service', :'journal_password'),
  ('performance_service', :'performance_password'),
  ('discipline_service', :'discipline_password'),
  ('reminder_service', :'reminder_password'),
  ('stock_research_service', :'stock_research_password'),
  ('market_data_service', :'market_data_password'),
  ('price_alert_service', :'price_alert_password'),
  ('rotation_service', :'rotation_password'),
  ('partner_service', :'partner_password'),
  ('content_service', :'content_password'),
  ('operations_service', :'operations_password')
) AS credentials(role_name, new_password)
\gexec
DO $$
BEGIN
  IF EXISTS (
    SELECT 1 FROM previous_role_verifiers old
    JOIN pg_authid current USING (rolname)
    WHERE old.rolpassword IS NOT DISTINCT FROM current.rolpassword
  ) THEN
    RAISE EXCEPTION 'One or more PostgreSQL role verifiers did not change';
  END IF;
END
$$;
COMMIT;
SQL

SECRET_ENV_FILE="$new_file" K8S_NAMESPACE="$namespace" \
  scripts/provision-k8s-secrets.sh --env-file "$new_file" --namespace "$namespace"

deployments='identity journal performance discipline reminder stock-research market-data price-alert rotation partner content operations'
for deployment in $deployments; do
  kubectl rollout restart deployment/"$deployment" -n "$namespace" >/dev/null
done
for deployment in $deployments; do
  kubectl rollout status deployment/"$deployment" -n "$namespace" --timeout=300s
done

for mapping in \
  trade_diary:POSTGRES_PASSWORD trade_diary_migrator:MIGRATOR_DB_PASSWORD \
  identity_service:IDENTITY_DB_PASSWORD journal_service:JOURNAL_DB_PASSWORD \
  performance_service:PERFORMANCE_DB_PASSWORD discipline_service:DISCIPLINE_DB_PASSWORD \
  reminder_service:REMINDER_DB_PASSWORD stock_research_service:STOCK_RESEARCH_DB_PASSWORD \
  market_data_service:MARKET_DATA_DB_PASSWORD price_alert_service:PRICE_ALERT_DB_PASSWORD \
  rotation_service:ROTATION_DB_PASSWORD partner_service:PARTNER_DB_PASSWORD \
  content_service:CONTENT_DB_PASSWORD operations_service:OPERATIONS_DB_PASSWORD
do
  role=${mapping%%:*}
  key=${mapping#*:}
  kubectl exec -n "$namespace" deployment/postgres -- \
    env PGPASSWORD="$(value "$key")" psql -h 127.0.0.1 -U "$role" -d trade_diary \
    -v ON_ERROR_STOP=1 -Atqc 'SELECT 1' >/dev/null
done

echo "Credential rotation, rollout, readiness, new-role connection, and old-verifier invalidation checks completed."
