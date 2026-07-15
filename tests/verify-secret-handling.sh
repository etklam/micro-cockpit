#!/bin/sh
set -eu

repo=$(CDPATH= cd -- "$(dirname "$0")/.." && pwd)
tmp=$(mktemp -d "${TMPDIR:-/tmp}/secret-handling-test.XXXXXX")
cleanup() { rm -rf "$tmp"; }
trap cleanup EXIT HUP INT TERM
mkdir "$tmp/bin" "$tmp/work"

cat >"$tmp/bin/kubectl" <<'EOF'
#!/bin/sh
case "$*" in
  *'create secret generic'*) printf '%s\n' 'apiVersion: v1' 'kind: Secret' 'metadata: {name: test}' ;;
  *) : ;;
esac
EOF
chmod 700 "$tmp/bin/kubectl"

before=$(git -C "$repo" status --porcelain)
if PATH="$tmp/bin:$PATH" TMPDIR="$tmp/work" "$repo/scripts/provision-k8s-secrets.sh" \
  --env-file "$repo/k8s/secrets.example.env" >/dev/null 2>&1; then
  echo "Provisioning unexpectedly accepted placeholder secrets." >&2
  exit 1
fi
if find "$tmp/work" -mindepth 1 -print -quit | grep -q .; then
  echo "Provisioning left temporary secret files after failure." >&2
  exit 1
fi

valid="$tmp/generated-test-input.secret.env"
for key in POSTGRES_PASSWORD MIGRATOR_DB_PASSWORD IDENTITY_DB_PASSWORD JOURNAL_DB_PASSWORD PERFORMANCE_DB_PASSWORD DISCIPLINE_DB_PASSWORD REMINDER_DB_PASSWORD STOCK_RESEARCH_DB_PASSWORD MARKET_DATA_DB_PASSWORD PRICE_ALERT_DB_PASSWORD ROTATION_DB_PASSWORD PARTNER_DB_PASSWORD CONTENT_DB_PASSWORD OPERATIONS_DB_PASSWORD LOCAL_REGISTRATION_KEY INTERNAL_SERVICE_KEY; do
  printf '%s=TEST-ONLY-NOT-A-SECRET-%s\n' "$key" "$key" >>"$valid"
done
chmod 600 "$valid"
PATH="$tmp/bin:$PATH" TMPDIR="$tmp/work" "$repo/scripts/provision-k8s-secrets.sh" \
  --env-file "$valid" >/dev/null
if find "$tmp/work" -mindepth 1 -print -quit | grep -q .; then
  echo "Provisioning left temporary secret files after success." >&2
  exit 1
fi
after=$(git -C "$repo" status --porcelain)
[ "$before" = "$after" ] || { echo "Secret verification changed git status." >&2; exit 1; }

python3 "$repo/scripts/verify-no-plaintext-k8s-secrets.py"
echo "Secret handling tests passed."
