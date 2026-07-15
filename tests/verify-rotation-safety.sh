#!/usr/bin/env bash
set -euo pipefail

repo=$(CDPATH= cd -- "$(dirname "$0")/.." && pwd)
root=$(mktemp -d "${TMPDIR:-/tmp}/rotation-safety-test.XXXXXX")
cleanup() {
  if [ "${KEEP_ROTATION_TEST_TMP:-0}" = 1 ]; then
    echo "Rotation test artifacts retained at: $root" >&2
  else
    rm -rf "$root"
  fi
}
trap cleanup EXIT HUP INT TERM

keys=(POSTGRES_PASSWORD MIGRATOR_DB_PASSWORD IDENTITY_DB_PASSWORD JOURNAL_DB_PASSWORD PERFORMANCE_DB_PASSWORD DISCIPLINE_DB_PASSWORD REMINDER_DB_PASSWORD STOCK_RESEARCH_DB_PASSWORD MARKET_DATA_DB_PASSWORD PRICE_ALERT_DB_PASSWORD ROTATION_DB_PASSWORD PARTNER_DB_PASSWORD CONTENT_DB_PASSWORD OPERATIONS_DB_PASSWORD LOCAL_REGISTRATION_KEY INTERNAL_SERVICE_KEY)

make_fixture() {
  local directory=$1
  mkdir -p "$directory/bin" "$directory/work" "$directory/fixtures"
  printf '%s' old >"$directory/db-state"
  printf '%s' old >"$directory/app-state"
  : >"$directory/calls.log"
  : >"$directory/old.env"
  for key in "${keys[@]}"; do
    printf '%s=TEST-ONLY-OLD-MATERIAL-%s\n' "$key" "$key" >>"$directory/old.env"
  done
  python3 - "$directory" <<'PY'
import base64, json, pathlib, sys
root = pathlib.Path(sys.argv[1])
values = {}
for line in (root / "old.env").read_text().splitlines():
    key, value = line.split("=", 1)
    values[key] = base64.b64encode(value.encode()).decode()
db = {key: value for key, value in values.items() if key not in {"LOCAL_REGISTRATION_KEY", "INTERNAL_SERVICE_KEY"}}
app = {key: values[key] for key in ("LOCAL_REGISTRATION_KEY", "INTERNAL_SERVICE_KEY")}
connections = {"IDENTITY_CONNECTION_STRING": base64.b64encode(b"TEST-ONLY-CONNECTION").decode()}
for name, data in (("db-credentials", db), ("app-secrets", app), ("service-connection-strings", connections)):
    (root / "fixtures" / f"{name}.json").write_text(json.dumps({"apiVersion":"v1","kind":"Secret","metadata":{"name":name},"data":data}))
PY

  cat >"$directory/bin/openssl" <<'EOF'
#!/usr/bin/env bash
set -eu
counter_file="$TEST_ROOT/openssl-counter"
counter=0; [ ! -f "$counter_file" ] || counter=$(cat "$counter_file")
counter=$((counter + 1)); printf '%s' "$counter" >"$counter_file"
printf 'TEST-ONLY-NEW-MATERIAL-%02d' "$counter"
EOF

  cat >"$directory/bin/flock" <<'EOF'
#!/usr/bin/env python3
import fcntl
import sys

fcntl.flock(int(sys.argv[3]), fcntl.LOCK_EX)
EOF

  cat >"$directory/bin/curl" <<'EOF'
#!/usr/bin/env bash
set -eu
case "$*" in *'/health/ready'*) exit 0 ;; esac
header=""
previous=""
for argument in "$@"; do
  if [ "$previous" = -H ]; then
    case "$argument" in @*) header=${argument#@} ;; esac
  fi
  previous=$argument
done
material=$(cat "$header")
state=$(cat "$TEST_ROOT/app-state")
case "$material:$state" in
  *TEST-ONLY-OLD-MATERIAL*:old|*TEST-ONLY-NEW-MATERIAL*:new) printf '%s' 400 ;;
  *) printf '%s' 403 ;;
esac
EOF

  cat >"$directory/bin/kubectl" <<'EOF'
#!/usr/bin/env bash
set -eu
printf '%s\n' "$*" >>"$TEST_ROOT/calls.log"
case "${1:-} ${2:-}" in
  'config current-context') printf '%s\n' test-context; exit 0 ;;
  'get namespace') exit 0 ;;
  'get secret') cat "$TEST_ROOT/fixtures/$3.json"; exit 0 ;;
  'get deployment') printf '%s' 1; exit 0 ;;
  'port-forward -n') while :; do sleep 1; done ;;
  'create secret')
    name=$4
    for argument in "$@"; do
      case "$argument" in
        --from-env-file=*)
          file=${argument#*=}
          if [ "$name" = app-secrets ] && grep -q 'TEST-ONLY-NEW-MATERIAL' "$file"; then printf '%s' new >"$TEST_ROOT/app-state"; fi
          ;;
      esac
    done
    printf '%s\n' 'apiVersion: v1' 'kind: Secret' 'metadata: {name: test}'
    exit 0
    ;;
  'apply --namespace') cat >/dev/null || true; exit 0 ;;
  'patch secret')
    printf 'restore-secret:%s\n' "$3" >>"$TEST_ROOT/events.log"
    [ "$3" != app-secrets ] || printf '%s' old >"$TEST_ROOT/app-state"
    exit 0
    ;;
  'exec -i')
    input=$(cat)
    case "$*" in
      *' sh -c '*)
        role=${!#}
        state=$(cat "$TEST_ROOT/db-state")
        case "$input:$state" in
          *TEST-ONLY-OLD-MATERIAL*:old|*TEST-ONLY-NEW-MATERIAL*:new)
            printf 'login:%s:accepted\n' "$role" >>"$TEST_ROOT/events.log"; exit 0 ;;
          *) printf 'login:%s:rejected\n' "$role" >>"$TEST_ROOT/events.log"; exit 1 ;;
        esac
        ;;
      *' psql -U trade_diary '*)
        state=$(cat "$TEST_ROOT/db-state")
        if [ "$state" = old ]; then next=new; else next=old; fi
        printf '%s' "$next" >"$TEST_ROOT/db-state"
        printf 'db-state:%s\n' "$next" >>"$TEST_ROOT/events.log"
        exit 0
        ;;
    esac
    ;;
esac
exit 0
EOF
  chmod 700 "$directory/bin/openssl" "$directory/bin/curl" "$directory/bin/kubectl" "$directory/bin/flock"
}

run_case() {
  local name=$1 fault=$2 expected=$3 rc=0 directory
  directory="$root/$name"
  make_fixture "$directory"
  : >"$directory/events.log"
  (
    cd "$repo"
    PATH="$directory/bin:$PATH" TMPDIR="$directory/work" TEST_ROOT="$directory" \
      ROTATION_TEST_MODE=1 ROTATION_TEST_PORT=18080 ROTATION_FAULT_INJECTION="$fault" \
      scripts/rotate-k8s-credentials.sh --namespace micro-cockpit \
        --context test-context --backup-confirmed TEST-BACKUP-REFERENCE
  ) >"$directory/output.log" 2>&1 || rc=$?
  if [ "$expected" = success ]; then [ "$rc" -eq 0 ]; else [ "$rc" -ne 0 ]; fi
  ! grep -Eq 'TEST-ONLY-(OLD|NEW)-MATERIAL' "$directory/output.log"
  ! find "$directory/work" -mindepth 1 -name 'micro-cockpit-rotation.*' -print -quit | grep -q .
}

run_case before-db before-db failure
! grep -q '^db-state:' "$root/before-db/events.log"

for stage in after-db after-secrets during-rollout; do
  run_case "$stage" "$stage" failure
  grep -q '^db-state:new$' "$root/$stage/events.log"
  grep -q '^db-state:old$' "$root/$stage/events.log"
  grep -q '^restore-secret:db-credentials$' "$root/$stage/events.log"
  grep -q '^restore-secret:service-connection-strings$' "$root/$stage/events.log"
  grep -q '^restore-secret:app-secrets$' "$root/$stage/events.log"
  grep -q 'rollout restart' "$root/$stage/calls.log"
done

run_case success '' success
grep -q '^db-state:new$' "$root/success/events.log"
! grep -q '^db-state:old$' "$root/success/events.log"
[ "$(grep -c ':rejected$' "$root/success/events.log")" -eq 14 ]

echo "Credential rotation success, negative-verification, rollback, and cleanup tests passed."
