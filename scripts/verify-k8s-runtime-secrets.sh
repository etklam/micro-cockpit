#!/usr/bin/env bash
set -euo pipefail

namespace="${K8S_NAMESPACE:-micro-cockpit}"

usage() {
  echo "Usage: $0 [--namespace NAME]" >&2
}

while [ "$#" -gt 0 ]; do
  case "$1" in
    --namespace) namespace=${2:?missing namespace}; shift 2 ;;
    *) usage; exit 2 ;;
  esac
done

[[ "$namespace" =~ ^[a-z0-9]([-a-z0-9]*[a-z0-9])?$ ]] || { echo "Invalid namespace." >&2; exit 1; }
command -v kubectl >/dev/null 2>&1 || { echo "kubectl is required." >&2; exit 1; }

verify_secret() {
  local name=$1
  shift
  if ! kubectl get secret "$name" -n "$namespace" >/dev/null 2>&1; then
    echo "Missing runtime Secret: $name" >&2
    return 1
  fi

  local key encoded failed=0
  for key in "$@"; do
    encoded=$(kubectl get secret "$name" -n "$namespace" -o "jsonpath={.data.${key}}" 2>/dev/null) || encoded=""
    if [ -z "$encoded" ]; then
      echo "Missing or empty runtime Secret key: $name/$key" >&2
      failed=1
    fi
  done
  return "$failed"
}

failed=0
verify_secret db-credentials \
  POSTGRES_PASSWORD MIGRATOR_DB_PASSWORD IDENTITY_DB_PASSWORD JOURNAL_DB_PASSWORD \
  PERFORMANCE_DB_PASSWORD DISCIPLINE_DB_PASSWORD REMINDER_DB_PASSWORD \
  STOCK_RESEARCH_DB_PASSWORD MARKET_DATA_DB_PASSWORD PRICE_ALERT_DB_PASSWORD \
  ROTATION_DB_PASSWORD PARTNER_DB_PASSWORD CONTENT_DB_PASSWORD OPERATIONS_DB_PASSWORD || failed=1
verify_secret service-connection-strings \
  IDENTITY_CONNECTION_STRING JOURNAL_CONNECTION_STRING PERFORMANCE_CONNECTION_STRING \
  DISCIPLINE_CONNECTION_STRING REMINDER_CONNECTION_STRING STOCK_RESEARCH_CONNECTION_STRING \
  MARKET_DATA_CONNECTION_STRING PRICE_ALERT_CONNECTION_STRING ROTATION_CONNECTION_STRING \
  PARTNER_CONNECTION_STRING CONTENT_CONNECTION_STRING OPERATIONS_CONNECTION_STRING || failed=1
verify_secret app-secrets LOCAL_REGISTRATION_KEY INTERNAL_SERVICE_KEY || failed=1

[ "$failed" -eq 0 ] || exit 1
echo "Required runtime Secrets are present and complete in namespace $namespace."
