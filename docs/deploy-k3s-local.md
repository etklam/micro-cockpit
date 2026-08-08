# Local-to-K3s deployment runbook

This runbook documents how an operator deploys Micro Cockpit from a local
checkout to the production single-node K3s cluster.

| Item | Value |
|------|-------|
| Target host | `82.22.63.196` |
| Namespace | `micro-cockpit` |
| Registry image base | `git.913555.xyz/etklam/micro-cockpit` |
| Public URL | `https://beta.trade-basic.com` |
| Deploy entrypoint | `.forgejo/workflows/deploy.yml` |

The normal path is the Forgejo `build-and-deploy` workflow. The manual path
below is useful when the workflow runner is unavailable or when an operator
needs to reproduce a release from a local machine. Both paths use the same
repository scripts and the same immutable image tags.

## 1. Release rules

- Build every image with the same full, lowercase 40-character Git commit SHA.
- Never use `latest`, a shortened SHA, or a mutable release tag.
- The database tooling image is deployed first through the immutable bootstrap,
  migrate, and finalize Jobs.
- The six application Deployments are `identity`, `journal`, `market-data`,
  `tool`, `edge`, and `frontend`.
- Runtime credentials are read from the existing Kubernetes Secrets. A normal
  release must not create, replace, or print those Secrets.
- Release, database upgrade, baseline, and credential rotation must run inside
  the namespace operation lock.

## 2. Prerequisites

Local operator machine:

- Git, Docker with a running daemon, `ssh`, `scp`, `curl`, and `jq`.
- Docker must be able to build `linux/amd64` images. On Apple Silicon this
  means using `docker build --platform linux/amd64` as shown below.
- A verified SSH host key for the deployment host. Do not disable host-key
  checking or populate the key from an unverified `ssh-keyscan` result.
- Push credentials for the Forgejo container registry. Keep them in the
  credential manager or an environment variable; never commit them.

Remote K3s host:

- K3s, `kubectl`, `flock`, and Python 3 are installed and usable by the deploy
  identity.
- `/run/lock/micro-cockpit` exists as a real, non-world-writable directory and
  is writable by the deploy identity.
- The `db-credentials`, `service-connection-strings`, and `app-secrets`
  Secrets already exist in namespace `micro-cockpit`.
- PostgreSQL, the ingress controller, cert-manager, and the TLS certificate
  are already provisioned. Bare-server setup is in [deploy-k3s.md](deploy-k3s.md).

Use a dedicated deploy account for production. The current beta host may still
accept a legacy operator account, but `root` should not be used for a permanent
CI credential.

## 3. Local preflight

Run these checks from the repository root before building:

```sh
set -eu

git status --short
IMAGE_TAG="$(git rev-parse HEAD)"
test "$(git rev-parse --verify HEAD)" = "$IMAGE_TAG"
printf 'release: %s\n' "$IMAGE_TAG"

bash -n scripts/*.sh
python3 scripts/validate-migrations.py
python3 scripts/audit-migrations.py
python3 scripts/verify-no-plaintext-k8s-secrets.py
./tests/verify-secret-handling.sh
./tests/verify-deployment-safety.sh
./tests/verify-runtime-secret-source.sh
./tests/verify-k8s-operation-lock.sh
./tests/verify-migration-safety.sh
```

Do not continue if the checkout has unexpected changes or if any preflight
check fails. The release SHA used below must be the SHA whose source was
checked out and whose images are built.

## 4. Preferred deployment: Forgejo

Pushing a commit to `main` starts `.forgejo/workflows/deploy.yml`. The workflow
does the following in order:

1. Checks out the exact Forgejo commit.
2. Runs migration, secret-handling, deployment-safety, and lock checks.
3. Builds and pushes `db-migrator`, `identity`, `journal`, `market-data`,
   `tool`, `edge`, and `frontend`, all tagged with the full commit SHA.
4. SSHes to the K3s host using a pinned host key.
5. Verifies runtime Secrets, applies cluster infrastructure, and runs database
   upgrade Jobs under the namespace operation lock.
6. Applies the immutable application release and waits for every rollout.

Use the Forgejo workflow as the source of truth when changing this process.
The manual commands below intentionally mirror its remote sequence.

## 5. Build and push images locally

Log in to the registry without putting the token in the shell history:

```sh
printf '%s' "$FORGEJO_REGISTRY_TOKEN" \
  | docker login git.913555.xyz \
      --username "$FORGEJO_REGISTRY_USER" \
      --password-stdin
```

Build all seven images for the K3s node architecture:

```sh
set -eu

IMAGE_BASE='git.913555.xyz/etklam/micro-cockpit'
IMAGE_TAG="$(git rev-parse HEAD)"

build_and_push() {
  name="$1"
  dockerfile="$2"
  image="${IMAGE_BASE}/${name}:${IMAGE_TAG}"
  docker build --platform linux/amd64 -t "$image" -f "$dockerfile" .
  docker push "$image"
}

build_and_push db-migrator platform/postgres/Dockerfile
build_and_push identity services/identity-service/src/TradeDiary.Identity/Dockerfile
build_and_push journal services/journal-service/src/TradeDiary.Journal/Dockerfile
build_and_push market-data services/market-data-service/src/TradeDiary.MarketData/Dockerfile
build_and_push tool services/tool-service/src/TradeDiary.Tool/Dockerfile
build_and_push edge gateway/TradeDiary.EdgeApi/Dockerfile
build_and_push frontend frontend/Dockerfile
```

Do not run the deploy step until all seven pushes have succeeded. The cluster
must be able to authenticate to `git.913555.xyz`; an image that exists only in
the local Docker daemon is not a deployable release.

## 6. Deploy the exact release over SSH

The remote helper scripts need only the manifests and the deployment/database
helpers. The following command packages those files into a temporary local
artifact, copies them to the host, and runs the same locked sequence as CI.
Use a verified entry in the local SSH `known_hosts` file or add an explicit
`UserKnownHostsFile` pointing at an operator-managed file.

```sh
set -euo pipefail

DEPLOY_HOST='82.22.63.196'
DEPLOY_USER='micro-cockpit-deploy'
DEPLOY_NAMESPACE='micro-cockpit'
IMAGE_REGISTRY='git.913555.xyz/etklam/micro-cockpit'
IMAGE_TAG="$(git rev-parse HEAD)"
SSH_OPTS=(-o StrictHostKeyChecking=yes)

work="$(mktemp -d "${TMPDIR:-/tmp}/micro-cockpit-local-deploy.XXXXXX")"
remote=''
cleanup() {
  if [ -n "$remote" ]; then
    ssh "${SSH_OPTS[@]}" "${DEPLOY_USER}@${DEPLOY_HOST}" \
      "rm -rf -- '$remote'" >/dev/null 2>&1 || true
  fi
  rm -rf "$work"
}
trap cleanup EXIT HUP INT TERM

tar -czf "$work/deploy.tgz" \
  k8s \
  scripts/apply-k8s-manifests.sh \
  scripts/deploy-k8s-release.sh \
  scripts/run-k8s-database-upgrade.sh \
  scripts/k8s-database-job.py \
  scripts/k8s-database-job-lib.sh \
  scripts/verify-k8s-runtime-secrets.sh \
  scripts/with-k8s-operation-lock.sh

remote="$(ssh "${SSH_OPTS[@]}" "${DEPLOY_USER}@${DEPLOY_HOST}" \
  'mktemp -d /tmp/micro-cockpit-deploy.XXXXXX')"
scp "${SSH_OPTS[@]}" "$work/deploy.tgz" \
  "${DEPLOY_USER}@${DEPLOY_HOST}:${remote}/deploy.tgz"

ssh "${SSH_OPTS[@]}" "${DEPLOY_USER}@${DEPLOY_HOST}" \
  "REMOTE_DIR='$remote' DEPLOY_NAMESPACE='$DEPLOY_NAMESPACE' IMAGE_REGISTRY='$IMAGE_REGISTRY' IMAGE_TAG='$IMAGE_TAG' bash -s" <<'REMOTE'
set -euo pipefail
cd "$REMOTE_DIR"
tar -xzf deploy.tgz
chmod 700 \
  scripts/apply-k8s-manifests.sh \
  scripts/deploy-k8s-release.sh \
  scripts/run-k8s-database-upgrade.sh \
  scripts/k8s-database-job.py \
  scripts/verify-k8s-runtime-secrets.sh \
  scripts/with-k8s-operation-lock.sh

scripts/with-k8s-operation-lock.sh \
  --namespace "$DEPLOY_NAMESPACE" \
  --timeout 900 \
  -- bash -c '
    set -euo pipefail
    scripts/verify-k8s-runtime-secrets.sh --namespace "$DEPLOY_NAMESPACE"
    scripts/apply-k8s-manifests.sh --namespace "$DEPLOY_NAMESPACE" --skip-services
    scripts/run-k8s-database-upgrade.sh \
      --namespace "$DEPLOY_NAMESPACE" \
      --image-registry "$IMAGE_REGISTRY" \
      --image-tag "$IMAGE_TAG"
    scripts/deploy-k8s-release.sh \
      --namespace "$DEPLOY_NAMESPACE" \
      --image-registry "$IMAGE_REGISTRY" \
      --image-tag "$IMAGE_TAG" \
      --skip-infrastructure
  '
REMOTE
```

The deploy helper rejects shortened or mutable tags, renders a Kustomize
overlay, applies the six application Deployments, waits up to five minutes per
rollout, and verifies each image and release annotation. A database Job that
already completed with the same verified specification is safely reused on a
repeat deployment.

## 7. Post-deploy verification

Run the following on the K3s host, or prefix each command with SSH:

```sh
kubectl get deployments -n micro-cockpit \
  -o custom-columns='NAME:.metadata.name,READY:.status.readyReplicas,AVAILABLE:.status.availableReplicas,IMAGE:.spec.template.spec.containers[0].image'

kubectl get pods -n micro-cockpit -o wide

kubectl get deployment -n micro-cockpit \
  -o jsonpath='{range .items[*]}{.metadata.name}{"="}{.metadata.annotations.micro-cockpit\/deployed-sha}{"\n"}{end}'

kubectl get deployment identity -n micro-cockpit \
  -o jsonpath='{.spec.template.spec.containers[0].env[?(@.name=="Auth__AllowPublicRegistration")].value}'
printf '\n'

test "$(kubectl get pods -n micro-cockpit \
  -o jsonpath='{range .items[*]}{range .status.containerStatuses[*]}{.state.waiting.reason}{"\n"}{end}{end}' \
  | grep -c '^CrashLoopBackOff$' || true)" -eq 0

# The application readiness/liveness probes check the internal Edge endpoint;
# the public ingress exposes the frontend and its /api proxy.
curl -fsSI https://beta.trade-basic.com/
unauthenticated_refresh_status="$(curl -sS -o /dev/null -w '%{http_code}' \
  -X POST https://beta.trade-basic.com/api/auth/refresh)"
test "$unauthenticated_refresh_status" = 401
printf 'unauthenticated refresh status=%s (expected)\n' "$unauthenticated_refresh_status"

# After signing in as a normal user in the browser, /api/app/bootstrap must
# return 200. Do not put an access token in this runbook or shell history.
```

Expected results:

- all six application Deployments have one Ready/Available replica;
- every application image ends in the deployed full SHA;
- no Pod is in `CrashLoopBackOff`;
- the identity deployment reports `true` for public registration;
- the public frontend returns HTTP 200 and unauthenticated refresh returns the
  expected 401; after a normal-user login, `/api/app/bootstrap` returns 200;
  Kubernetes readiness/liveness probes cover the internal Edge health endpoints.

For the release tag used by a deployment, also verify the database Jobs:

```sh
IMAGE_TAG="$(kubectl get deployment edge -n micro-cockpit \
  -o jsonpath='{.metadata.annotations.micro-cockpit\/deployed-sha}')"

kubectl get jobs -n micro-cockpit \
  -l "micro-cockpit/release-sha=$IMAGE_TAG" \
  -o custom-columns='NAME:.metadata.name,COMPLETIONS:.status.succeeded,FAILED:.status.failed'
```

## 8. Rollback

Rollback application code by redeploying a previously verified image SHA. Do
not use `kubectl edit` or `latest`:

```sh
scripts/deploy-k8s-release.sh \
  --namespace micro-cockpit \
  --image-registry git.913555.xyz/etklam/micro-cockpit \
  --image-tag PREVIOUS_FULL_COMMIT_SHA \
  --skip-infrastructure
```

Application rollback does not roll back PostgreSQL migrations. Only choose an
older application image that is compatible with the schema already present.
Database baseline and credential rotation are separate, explicitly confirmed
operator operations.

## 9. Troubleshooting

| Symptom | Check |
|---------|-------|
| `Image tag must be a full lowercase commit SHA` | Use `git rev-parse HEAD`, not `git rev-parse --short HEAD`. |
| `Missing runtime Secret` | Run `scripts/verify-k8s-runtime-secrets.sh --namespace micro-cockpit` on the host; provision Secrets separately. |
| Lock directory error | Recreate `/run/lock/micro-cockpit` as a non-world-writable directory; see [deploy-k3s.md](deploy-k3s.md). |
| `ImagePullBackOff` | Confirm all seven SHA-tagged images were pushed and the K3s node can authenticate to the registry. |
| Rollout timeout | Inspect `kubectl describe deployment NAME -n micro-cockpit` and `kubectl logs deployment/NAME -n micro-cockpit --all-containers`. |
| `53300` PostgreSQL connection-slot error | Do not retry blindly; follow the baseline/connection-slot procedure in [deploy-k3s.md](deploy-k3s.md). |

Never put a production `.env`, registry token, SSH private key, or rendered
Secret manifest in this repository. Local deployment bundles belong under
`.deploy/` or a system temporary directory; those paths are ignored by
`.gitignore`.
