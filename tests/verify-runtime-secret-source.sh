#!/usr/bin/env bash
set -euo pipefail

repo=$(CDPATH= cd -- "$(dirname "$0")/.." && pwd)
workflow="$repo/.forgejo/workflows/deploy.yml"
tmp=$(mktemp -d "${TMPDIR:-/tmp}/runtime-secret-source-test.XXXXXX")
cleanup() { rm -rf "$tmp"; }
trap cleanup EXIT HUP INT TERM
mkdir "$tmp/bin"

runtime_names='POSTGRES_PASSWORD|MIGRATOR_DB_PASSWORD|IDENTITY_DB_PASSWORD|JOURNAL_DB_PASSWORD|PERFORMANCE_DB_PASSWORD|DISCIPLINE_DB_PASSWORD|REMINDER_DB_PASSWORD|STOCK_RESEARCH_DB_PASSWORD|MARKET_DATA_DB_PASSWORD|PRICE_ALERT_DB_PASSWORD|ROTATION_DB_PASSWORD|PARTNER_DB_PASSWORD|CONTENT_DB_PASSWORD|TOOL_DB_PASSWORD|OPERATIONS_DB_PASSWORD|LOCAL_REGISTRATION_KEY|INTERNAL_SERVICE_KEY'
if grep -Eq "$runtime_names" "$workflow"; then
  echo "Normal deployment references application runtime credentials." >&2
  exit 1
fi
! grep -q 'deployment.secret.env' "$workflow"
! grep -q 'provision-k8s-secrets.sh' "$workflow"
grep -q 'verify-k8s-runtime-secrets.sh' "$workflow"
grep -q 'with-k8s-operation-lock.sh' "$workflow"
! grep -Eq 'kind:[[:space:]]*Secret|stringData:' "$repo"/k8s/*.yaml
if awk 'NF && $0 !~ /^#/ && $0 !~ /^[A-Z0-9_]+=REQUIRED$/ { exit 1 }' "$repo/k8s/secrets.example.env"; then
  :
else
  echo "Kubernetes example credentials contain a usable value." >&2
  exit 1
fi

cat >"$tmp/bin/kubectl" <<'EOF'
#!/usr/bin/env bash
set -eu
printf '%s\n' "$*" >>"$KUBECTL_LOG"
[ "${1:-}" = get ] && [ "${2:-}" = secret ] || exit 77
name=$3
if [ "${MISSING_SECRET:-}" = "$name" ]; then exit 1; fi
case "$*" in
  *'-o jsonpath='*)
    previous=""
    expression=""
    for argument in "$@"; do
      if [ "$previous" = -o ]; then expression=$argument; break; fi
      previous=$argument
    done
    key=${expression##*.}
    key=${key%\}}
    if [ "${MISSING_KEY:-}" = "$name/$key" ] || [ "${EMPTY_KEY:-}" = "$name/$key" ]; then exit 0; fi
    printf '%s' 'VEVTVC1PTkxZLU5PTkNF'
    ;;
esac
EOF
chmod 700 "$tmp/bin/kubectl"

run_verify() {
  : >"$tmp/kubectl.log"
  PATH="$tmp/bin:$PATH" KUBECTL_LOG="$tmp/kubectl.log" "$repo/scripts/verify-k8s-runtime-secrets.sh" \
    --namespace micro-cockpit
}

run_verify >"$tmp/output" 2>&1
! grep -q 'TEST-ONLY-NONCE' "$tmp/output"
! grep -Eq '(^| )(create|apply|patch|delete)( |$)' "$tmp/kubectl.log"

if MISSING_SECRET=db-credentials run_verify >"$tmp/output" 2>&1; then
  echo "Missing runtime Secret was accepted." >&2
  exit 1
fi
grep -q 'db-credentials' "$tmp/output"

if MISSING_KEY=app-secrets/INTERNAL_SERVICE_KEY run_verify >"$tmp/output" 2>&1; then
  echo "Missing runtime Secret key was accepted." >&2
  exit 1
fi
grep -q 'app-secrets/INTERNAL_SERVICE_KEY' "$tmp/output"

if EMPTY_KEY=db-credentials/POSTGRES_PASSWORD run_verify >"$tmp/output" 2>&1; then
  echo "Empty runtime Secret key was accepted." >&2
  exit 1
fi
grep -q 'db-credentials/POSTGRES_PASSWORD' "$tmp/output"
! grep -q 'TEST-ONLY-NONCE' "$tmp/output"

verify_line=$(grep -n 'verify-k8s-runtime-secrets.sh --namespace' "$workflow" | tail -1 | cut -d: -f1)
deploy_line=$(grep -n 'deploy-k8s-release.sh --namespace' "$workflow" | tail -1 | cut -d: -f1)
[ "$verify_line" -lt "$deploy_line" ]
grep -q 'with-k8s-operation-lock.sh' "$repo/scripts/rotate-k8s-credentials.sh"

echo "Runtime Secret source-of-truth tests passed."
