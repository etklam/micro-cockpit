#!/usr/bin/env bash
set -euo pipefail

repo=$(CDPATH= cd -- "$(dirname "$0")/.." && pwd)
workflow="$repo/.forgejo/workflows/deploy.yml"
tmp=$(mktemp -d "${TMPDIR:-/tmp}/migration-safety-test.XXXXXX")
cleanup() { rm -rf "$tmp"; }
trap cleanup EXIT HUP INT TERM
mkdir "$tmp/bin" "$tmp/applied"

python3 "$repo/scripts/validate-migrations.py"
python3 "$repo/scripts/audit-migrations.py" >/dev/null
! grep -q '/docker-entrypoint-initdb.d' "$repo/compose.yaml" "$repo"/k8s/*.yaml
! grep -Eqi 'CREATE (TABLE|SCHEMA|VIEW|FUNCTION)' "$repo"/k8s/*.yaml
grep -q 'build_and_push db-migrator platform/postgres/Dockerfile' "$workflow"
! grep -q ':latest' "$repo/scripts/run-k8s-database-upgrade.sh"
! grep -q 'baseline-k8s-database\|--confirm-existing-database' "$workflow"

upgrade_line=$(grep -n 'run-k8s-database-upgrade.sh --namespace' "$workflow" | tail -1 | cut -d: -f1)
deploy_line=$(grep -n 'deploy-k8s-release.sh --namespace' "$workflow" | tail -1 | cut -d: -f1)
[ "$upgrade_line" -lt "$deploy_line" ]

cat >"$tmp/bin/kubectl" <<'EOF'
#!/usr/bin/env bash
set -eu
case "${1:-} ${2:-}" in
  'rollout status') exit 0 ;;
  'get job') exit 1 ;;
  'apply -f') cp "$3" "$CAPTURE/$(basename "$3")"; exit 0 ;;
  'wait --for=condition=Complete') exit 0 ;;
esac
exit 0
EOF
chmod 700 "$tmp/bin/kubectl"
tag=0123456789abcdef0123456789abcdef01234567
PATH="$tmp/bin:$PATH" CAPTURE="$tmp/applied" "$repo/scripts/run-k8s-database-upgrade.sh" \
  --namespace micro-cockpit --image-registry registry.example.test/project --image-tag "$tag" >/dev/null
[ "$(find "$tmp/applied" -type f | wc -l | tr -d ' ')" -eq 3 ]
[ "$(grep -Rhc "db-migrator:$tag" "$tmp/applied"/* | awk '{s+=$1} END{print s}')" -eq 3 ]
[ "$(grep -Rh 'secretKeyRef' "$tmp/applied"/* | wc -l | tr -d ' ')" -ge 3 ]
! grep -Rqi 'password: [^}]' "$tmp/applied"

echo "Migration static and mocked Kubernetes orchestration tests passed."
