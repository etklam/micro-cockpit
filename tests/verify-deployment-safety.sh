#!/usr/bin/env bash
set -euo pipefail

repo=$(CDPATH= cd -- "$(dirname "$0")/.." && pwd)
workflow="$repo/.forgejo/workflows/deploy.yml"
tmp=$(mktemp -d "${TMPDIR:-/tmp}/deployment-safety-test.XXXXXX")
cleanup() { rm -rf "$tmp"; }
trap cleanup EXIT HUP INT TERM

if grep -q 'StrictHostKeyChecking=no' "$workflow"; then
  echo "SSH host verification is disabled." >&2
  exit 1
fi
grep -q 'StrictHostKeyChecking=yes' "$workflow"
grep -q 'UserKnownHostsFile="\$known_hosts"' "$workflow"
grep -q 'IdentitiesOnly=yes' "$workflow"
grep -q 'validate-deploy-ssh-inputs.sh' "$workflow"
grep -q 'ssh "\${ssh_options\[@\]}"' "$workflow"
grep -q 'scp -q "\${ssh_options\[@\]}"' "$workflow"
grep -q 'with-k8s-operation-lock.sh --namespace "$DEPLOY_NAMESPACE" --timeout 900' "$workflow"
grep -q 'verify-k8s-runtime-secrets.sh --namespace "$DEPLOY_NAMESPACE"' "$workflow"
grep -q 'cancel-in-progress: false' "$workflow"
! grep -Eq 'DEPLOY_USER:[[:space:]]*root|DEPLOY_HOST:[[:space:]]*[0-9]' "$workflow"
grep -q 'image="\${IMAGE_BASE}/\${name}:\${IMAGE_TAG}"' "$workflow"
! grep -q ':latest' "$repo/k8s/06-services.yaml"
[ "$(grep -c ':REQUIRED_IMAGE_TAG$' "$repo/k8s/06-services.yaml")" -eq 15 ]

if DEPLOY_HOST=host DEPLOY_USER=user DEPLOY_SSH_KEY=key DEPLOY_KNOWN_HOSTS= \
  "$repo/scripts/validate-deploy-ssh-inputs.sh" >/dev/null 2>&1; then
  echo "Empty DEPLOY_KNOWN_HOSTS was accepted." >&2
  exit 1
fi
DEPLOY_HOST=host DEPLOY_USER=deployer DEPLOY_SSH_KEY=key DEPLOY_KNOWN_HOSTS='host ssh-ed25519 TEST-ONLY-HOST-KEY' \
  "$repo/scripts/validate-deploy-ssh-inputs.sh" >/dev/null

mkdir "$tmp/bin"
real_kubectl=$(command -v kubectl)
cat >"$tmp/bin/kubectl" <<'EOF'
#!/usr/bin/env bash
set -eu
if [ "${1:-}" = kustomize ]; then
  exec "$REAL_KUBECTL" "$@"
fi
if [ "${1:-}" = apply ]; then
  for argument in "$@"; do
    case "$argument" in
      */release.yaml) cp "$argument" "$TEST_RENDERED" ;;
    esac
  done
  exit 0
fi
if [ "${1:-}" = get ] && [ "${2:-}" = deployment ]; then
  deployment=$3
  case "$*" in
    *containers*image*) printf '%s/%s:%s' "$TEST_REGISTRY" "$deployment" "$TEST_TAG" ;;
    *app*kubernetes*version*) printf '%s' "$TEST_TAG" ;;
    *deployed-sha*) printf '%s' "$TEST_TAG" ;;
    *conditions*) printf '%s' True ;;
  esac
  exit 0
fi
if [ "${1:-}" = get ] && [ "${2:-}" = pods ]; then exit 0; fi
exit 0
EOF
chmod 700 "$tmp/bin/kubectl"

tag=0123456789abcdef0123456789abcdef01234567
registry=registry.example.test/project
PATH="$tmp/bin:$PATH" REAL_KUBECTL="$real_kubectl" TEST_RENDERED="$tmp/rendered.yaml" \
  TEST_REGISTRY="$registry" TEST_TAG="$tag" \
  "$repo/scripts/deploy-k8s-release.sh" --namespace micro-cockpit \
    --image-registry "$registry" --image-tag "$tag" >/dev/null

[ "$(grep -c "image: ${registry}/.*:${tag}$" "$tmp/rendered.yaml")" -eq 15 ]
! grep -q 'image: .*:latest' "$tmp/rendered.yaml"
grep -q "micro-cockpit/deployed-sha: ${tag}" "$tmp/rendered.yaml"

if PATH="$tmp/bin:$PATH" "$repo/scripts/deploy-k8s-release.sh" \
  --image-registry "$registry" --image-tag latest >/dev/null 2>&1; then
  echo "Deployment helper accepted a mutable image tag." >&2
  exit 1
fi

echo "Deployment trust, locking, and immutable-release tests passed."
