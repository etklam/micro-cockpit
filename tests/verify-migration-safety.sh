#!/usr/bin/env bash
set -euo pipefail

repo=$(CDPATH= cd -- "$(dirname "$0")/.." && pwd)
workflow="$repo/.forgejo/workflows/deploy.yml"
python3 "$repo/scripts/validate-migrations.py"
python3 "$repo/scripts/audit-migrations.py" >/dev/null
python3 "$repo/tests/verify-migration-append-only.py"
python3 "$repo/tests/verify-k8s-database-jobs.py"
! grep -q '/docker-entrypoint-initdb.d' "$repo/compose.yaml" "$repo"/k8s/*.yaml
! grep -Eqi 'CREATE (TABLE|SCHEMA|VIEW|FUNCTION)' "$repo"/k8s/*.yaml
grep -q 'build_and_push db-migrator platform/postgres/Dockerfile' "$workflow"
grep -q 'fetch-depth: 0' "$workflow"
grep -q 'fetch-depth: 0' "$repo/.github/workflows/ci.yml"
! grep -q ':latest' "$repo/scripts/run-k8s-database-upgrade.sh"
! grep -q 'baseline-k8s-database\|--confirm-existing-database' "$workflow"
! grep -q -- '--retry-failed-jobs' "$workflow"

append_line=$(grep -n 'validate-migration-append-only.py --base-ref' "$workflow" | head -1 | cut -d: -f1)
build_line=$(grep -n 'build_and_push db-migrator' "$workflow" | head -1 | cut -d: -f1)
host_line=$(grep -n 'scripts/validate-deploy-ssh-inputs.sh' "$workflow" | head -1 | cut -d: -f1)
upgrade_line=$(grep -n 'run-k8s-database-upgrade.sh --namespace' "$workflow" | tail -1 | cut -d: -f1)
deploy_line=$(grep -n 'deploy-k8s-release.sh --namespace' "$workflow" | tail -1 | cut -d: -f1)
[ "$append_line" -lt "$build_line" ]
[ "$append_line" -lt "$host_line" ]
[ "$upgrade_line" -lt "$deploy_line" ]
grep -q 'micro-cockpit/job-spec-sha256' "$repo/scripts/k8s-database-job.py"
grep -q -- '--retry-failed-job' "$repo/scripts/baseline-k8s-database.sh"
grep -q 'status-k8s-database.sh' "$repo/docs/database-migrations.md"

echo "Migration static, history, and Kubernetes orchestration tests passed."
