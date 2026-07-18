# K3s deployment guide

This is the operator runbook for standing Micro Cockpit up on a single-node K3s
cluster from a bare server. It documents the **environment setup** steps that
`operations.md` assumes are already in place: the cluster itself, the ingress
and DNS plumbing, the deploy host account, the operation lock directory, the
runtime Secrets, and the initial database baseline.

Once the environment is provisioned, day-to-day releases flow through the
Forgejo `build-and-deploy` pipeline. Manual release, baseline, rotation, and
rollback remain operator actions and are documented in
[operations.md](operations.md).

> **Scope.** Single-node K3s on Ubuntu 24.04, with Cloudflare-managed DNS and
> Let's Encrypt via DNS-01. Multi-node clusters, other ingress controllers, and
> HTTP-01 challenges are out of scope.

---

## 0. Prerequisites

| Item | Requirement |
|------|-------------|
| Server | Ubuntu 24.04 LTS, 2 vCPU / 8 GB RAM minimum |
| DNS | A wildcard `*.trade-basic.com` is not required; ExternalDNS creates A records on demand |
| Cloudflare | API token with `Zone:DNS:Edit` for the `trade-basic.com` zone |
| Domain ownership | `trade-basic.com` (or substitute) delegated to Cloudflare nameservers |
| Registry | A Forgejo (or compatible) container registry with push credentials |
| Local tools | `kubectl`, `ssh`, `curl`, `jq` |

The deployment host is the same machine that runs K3s in this guide. Splitting
the runner from the cluster is possible but adds SSH hops not covered here.

---

## 1. Install K3s

Install the K3s server with the embedded Traefik and ServiceLB disabled (Traefik
is kept, ServiceLB is replaced by the host-network ingress below). Disable
`traefik` only if you plan to bring your own ingress; otherwise leave it on.

```sh
curl -sfL https://get.k3s.io | INSTALL_K3S_EXEC="server --disable=traefik" sh -
```

Verify the node is ready and grab the kubeconfig for local use:

```sh
kubectl get nodes
sudo cat /etc/rancher/k3s/k3s.yaml > ~/.kube/config
sudo chown "$(id -u):$(id -g)" ~/.kube/config
sed -i 's|127.0.0.1|SERVER_PUBLIC_IP|' ~/.kube/config
kubectl get pods -A
```

K3s embeds CoreDNS, Metrics Server, and the local-path provisioner. No further
cluster add-ons are required for this project.

---

## 2. Install cluster services

Micro Cockpit depends on three cluster-level services that are **not** part of
the namespace manifests: ExternalDNS, cert-manager with a Cloudflare issuer, and
Traefik (the K3s built-in).

### 2.1 Traefik

K3s ships Traefik unless `--disable=traefik` was passed. If you disabled it,
install the official Helm chart and enable the `websecure` entrypoint with TLS.

### 2.2 cert-manager

```sh
kubectl apply -f https://github.com/cert-manager/cert-manager/releases/download/v1.18.2/cert-manager.yaml
kubectl wait --for=condition=Available deployment -n cert-manager --all --timeout=120s
```

Create the Cloudflare API token secret and the `ClusterIssuer`. The token needs
`Zone:DNS:Edit` on the target zone and nothing else:

```sh
kubectl create secret generic cloudflare-api-token \
  --namespace cert-manager \
  --from-literal=api-token='CF_API_TOKEN_VALUE'
```

```yaml
# cluster-issuer-cloudflare.yaml
apiVersion: cert-manager.io/v1
kind: ClusterIssuer
metadata:
  name: letsencrypt-cloudflare
spec:
  acme:
    server: https://acme-v02.api.letsencrypt.org/directory
    email: ops@example.com
    privateKeySecretRef:
      name: letsencrypt-cloudflare-account
    solvers:
      - dns01:
          cloudflare:
            apiTokenSecretRef:
              name: cloudflare-api-token
              key: api-token
```

```sh
kubectl apply -f cluster-issuer-cloudflare.yaml
```

### 2.3 ExternalDNS

ExternalDNS watches `Ingress` and `Service` resources and creates the matching
Cloudflare DNS records. The token it needs is `Zone:DNS:Edit` plus
`Zone:Zone:Read` (so it can enumerate zones).

```sh
kubectl create secret generic cloudflare-api-token-externaldns \
  --from-literal=cloudflare_api_token='CF_DNS_TOKEN_VALUE'
```

```yaml
# externaldns.yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: external-dns
  namespace: default
spec:
  replicas: 1
  selector:
    matchLabels: { app: external-dns }
  template:
    metadata:
      labels: { app: external-dns }
    spec:
      containers:
        - name: external-dns
          image: registry.k8s.io/external-dns/external-dns:v0.15.1
          env:
            - name: CF_API_TOKEN
              valueFrom:
                secretKeyRef:
                  name: cloudflare-api-token-externaldns
                  key: cloudflare_api_token
          args:
            - --source=service
            - --source=ingress
            - --domain-filter=trade-basic.com
            - --provider=cloudflare
            - --policy=sync
            - --registry=txt
```

```sh
kubectl apply -f externaldns.yaml
```

When the project `Ingress` is applied later, ExternalDNS creates the A record
for `beta.trade-basic.com` automatically.

---

## 3. Create the deploy host account and SSH trust

Forgejo deploys by SSH-ing onto this host and running `kubectl` locally. Create
a dedicated account; **do not use `root` for production**. This guide used
`root` only because the host is a single-tenant beta.

```sh
sudo useradd -m -s /bin/bash micro-cockpit-deploy
sudo usermod -aG micro-cockpit-deploy-ops micro-cockpit-deploy
```

Install the deploy public key:

```sh
sudo -u micro-cockpit-deploy mkdir -p /home/micro-cockpit-deploy/.ssh
sudo -u micro-cockpit-deploy tee /home/micro-cockpit-deploy/.ssh/authorized_keys \
  < deploy_key.pub
sudo chmod 700 /home/micro-cockpit-deploy/.ssh
sudo chmod 600 /home/micro-cockpit-deploy/.ssh/authorized_keys
```

Capture the host's Ed25519 key for `DEPLOY_KNOWN_HOSTS`. **Obtain it directly
from the host administrator or the provider console and verify the fingerprint
out of band.** Do not auto-populate it from `ssh-keyscan` inside CI:

```sh
ssh-keygen -l -f /etc/ssh/ssh_host_ed25519_key.pub
cat /etc/ssh/ssh_host_ed25519_key.pub
# -> "SERVER_IP ssh-ed25519 AAAA..."
```

The deploy account needs `kubectl` access scoped to the `micro-cockpit`
namespace. The minimal `Role`/`RoleBinding` is: read Secret metadata/keys,
apply non-secret manifests, restart Deployments, read rollout and Pod status.
Bootstrap and rotation use a separate, more privileged operator identity.

---

## 4. Provision the operation lock directory

`scripts/with-k8s-operation-lock.sh` serialises every release, baseline, and
rotation through a file lock under `/run/lock/micro-cockpit`. The directory
**must exist before the first deploy**, must be a real directory (not a
symlink), must not be world-writable, and must be writable by the deploy and
operator identities.

```sh
sudo groupadd -f micro-cockpit-deploy-ops
sudo install -d -o root -g micro-cockpit-deploy-ops -m 2770 /run/lock/micro-cockpit
```

> **Persistent provisioner.** `/run` is a `tmpfs` on most Linux systems and is
> wiped on reboot. Persist this directory with a `systemd-tmpfiles` entry so a
> server reboot does not silently break the next deploy:
>
> ```sh
> sudo tee /etc/tmpfiles.d/micro-cockpit.conf >/dev/null <<'EOF'
> d /run/lock/micro-cockpit 2770 root micro-cockpit-deploy-ops -
> EOF
> sudo systemd-tmpfiles --create /etc/tmpfiles.d/micro-cockpit.conf
> ```
>
> A missing lock directory surfaces as
> `Kubernetes operation lock directory is not a directory: /run/lock/micro-cockpit`
> early in the deploy step.

`MICRO_COCKPIT_LOCK_DIR` overrides the path if a different location is
preferred.

---

## 5. Apply namespace and infrastructure manifests

The release helper (`scripts/deploy-k8s-release.sh`) refuses to deploy the
tracked `06-services.yaml` because its images carry the placeholder
`:REQUIRED_IMAGE_TAG`. Apply the infrastructure manifests separately, once:

```sh
scripts/apply-k8s-manifests.sh --namespace micro-cockpit --skip-services
```

This creates:

- `Namespace` micro-cockpit
- `PersistentVolumeClaim` for PostgreSQL data
- `PersistentVolumeClaim` for Identity's RSA signing key
- PostgreSQL `Deployment` + `Service`
- The project `Ingress` (host `beta.trade-basic.com`, TLS via the Cloudflare
  `ClusterIssuer`)

Wait for PostgreSQL to become ready before proceeding:

```sh
kubectl rollout status deployment/postgres -n micro-cockpit --timeout=300s
```

---

## 6. Bootstrap runtime Secrets

Micro Cockpit expects three Kubernetes Secrets in the production namespace.
They are the **single source of truth** for database credentials; the pipeline
never creates, patches, or replaces them during a normal release.

| Secret | Keys | Purpose |
|--------|------|---------|
| `db-credentials` | `POSTGRES_PASSWORD`, `MIGRATOR_DB_PASSWORD`, plus one `<SERVICE>_DB_PASSWORD` per service | PostgreSQL superuser and per-service role passwords |
| `service-connection-strings` | One `<SERVICE>_CONNECTION_STRING` per stateful service | Npgsql connection strings used by runtime pods |
| `app-secrets` | `LOCAL_REGISTRATION_KEY`, `INTERNAL_SERVICE_KEY` | Registration gate key and the shared internal service key |

Prepare a protected env file with every required value (template:
`k8s/secrets.example.env`), then run the provisioner from the deploy host:

```sh
scripts/provision-k8s-secrets.sh \
  --namespace micro-cockpit \
  --env-file /secure/path/production.secret.env \
  --confirm-create-or-replace
```

The connection-string format is `Host=postgres;Database=trade_diary;Username=<role>;Password=<password>`,
where `<role>` is the service-specific PostgreSQL role
(`identity_service`, `journal_service`, ...). The provisioner generates all 12
strings from the password values in the same env file.

Verify the three Secrets exist and have the expected key counts:

```sh
kubectl get secrets -n micro-cockpit
# db-credentials             Opaque   14      ...
# service-connection-strings Opaque   12      ...
# app-secrets                Opaque    2      ...
```

---

## 7. First database baseline

The migration runner refuses to migrate a database that has managed schemas but
no migration history. A fresh cluster is fine; an imported or pre-populated
database is not. The baseline step is an **explicit operator action** by design.

Take a verified backup first, then run the baseline workflow on the deploy host
from inside the operation lock:

```sh
scripts/baseline-k8s-database.sh \
  --context default \
  --namespace micro-cockpit \
  --image-registry REGISTRY/etklam/micro-cockpit \
  --image-tag FULL_COMMIT_SHA \
  --backup-confirmed BACKUP_REFERENCE \
  --confirm-existing-database
```

The `BACKUP_REFERENCE` is any opaque string that identifies the backup you took
(`pg_dump` file, snapshot ID, timestamp). It is recorded in `operations.job_registry`
for audit. `--confirm-existing-database` acknowledges that the schemas in place
are the ones being claimed as the baseline.

### Connection-slot starvation

If application Deployments are already running and crash-looping against a
database that has not yet been baselined, PostgreSQL may exhaust its
`max_connections` and the baseline Job fails with:

```
53300: remaining connection slots are reserved for roles with the SUPERUSER attribute
```

Scale the application Deployments to zero, run the baseline, then scale back up:

```sh
for dep in identity journal performance discipline reminder \
           stock-research market-data price-alert rotation \
           partner content tool operations edge frontend; do
  kubectl scale deployment/"$dep" -n micro-cockpit --replicas=0
done

# ... run baseline ...

for dep in identity journal performance discipline reminder \
           stock-research market-data price-alert rotation \
           partner content tool operations edge frontend; do
  kubectl scale deployment/"$dep" -n micro-cockpit --replicas=1
done
```

This is a chicken-and-egg problem unique to the first deploy of an environment
that has schemas but no history. Once the baseline is in place, migrations are
fast-forward and the running Deployments do not interfere.

---

## 8. First release

After infrastructure, Secrets, and baseline are in place, the first application
release applies all 15 service images with the same immutable commit SHA:

```sh
scripts/deploy-k8s-release.sh \
  --namespace micro-cockpit \
  --image-registry REGISTRY/etklam/micro-cockpit \
  --image-tag FULL_COMMIT_SHA \
  --skip-infrastructure
```

The helper renders a Kustomize overlay, rejects mutable (`:latest`) or shortened
tags, applies all Deployments together, waits for rollout, and verifies images,
annotations, availability, and the absence of `CrashLoopBackOff`.

After this point, subsequent releases are delivered by pushing to `main`; the
Forgejo `build-and-deploy` workflow builds, pushes, and deploys the new SHA
end-to-end.

---

## 9. Configure the Forgejo pipeline

`.forgejo/workflows/deploy.yml` reads four inputs from the repository:

| Input | Type | Source | Example |
|-------|------|--------|---------|
| `DEPLOY_HOST` | variable | Forgejo repo variables | `82.22.63.196` |
| `DEPLOY_USER` | variable | Forgejo repo variables | `micro-cockpit-deploy` |
| `DEPLOY_SSH_KEY` | secret | Forgejo repo secrets | deploy private key |
| `DEPLOY_KNOWN_HOSTS` | secret | Forgejo repo secrets | `host ssh-ed25519 AAAA...` |

Forgejo Actions API:

```sh
TOKEN='forgejo-api-token'

# variables (POST to create, PUT to replace)
curl -fsSL -H "Authorization: token $TOKEN" \
  -X POST "https://git.example/api/v1/repos/OWNER/REPO/actions/variables/DEPLOY_HOST" \
  -H "Content-Type: application/json" \
  -d '{"value":"82.22.63.196"}'

curl -fsSL -H "Authorization: token $TOKEN" \
  -X POST "https://git.example/api/v1/repos/OWNER/REPO/actions/variables/DEPLOY_USER" \
  -H "Content-Type: application/json" \
  -d '{"value":"micro-cockpit-deploy"}'

# secrets (PUT to create or replace)
curl -fsSL -H "Authorization: token $TOKEN" \
  -X PUT "https://git.example/api/v1/repos/OWNER/REPO/actions/secrets/DEPLOY_KNOWN_HOSTS" \
  -H "Content-Type: application/json" \
  -d '{"data":"82.22.63.196 ssh-ed25519 AAAA..."}'
```

Registry credentials (`REGISTRY_USER`, `REGISTRY_TOKEN`) are separate secrets
used only to push images. The pipeline also requires `kubectl` on the runner;
the workflow installs it on first use, but pinning the version in a custom
runner image is preferable for long-lived pools.

---

## 10. Verify the deployment

```sh
# All Deployments Ready
kubectl get deployments -n micro-cockpit

# No CrashLoopBackOff
kubectl get pods -n micro-cockpit \
  -o jsonpath='{range .items[*]}{range .status.containerStatuses[*]}{.state.waiting.reason}{"\n"}{end}{end}' \
  | grep -c '^CrashLoopBackOff$'   # expect: 0

# Ingress has a certificate
kubectl get certificate -n micro-cockpit

# Site responds
curl -sI https://beta.trade-basic.com/
```

Smoke-test the Edge health endpoints through the frontend proxy:

```sh
curl -s https://beta.trade-basic.com/api/app/bootstrap | jq .
```

---

## Troubleshooting

### `Missing required deployment input: DEPLOY_HOST`

The Forgejo repo is missing `DEPLOY_HOST` or `DEPLOY_USER` variables, or
`DEPLOY_SSH_KEY` / `DEPLOY_KNOWN_HOSTS` secrets. See section 9.

### `Kubernetes operation lock directory is not a directory`

`/run/lock/micro-cockpit` does not exist (often after a reboot) or is a
symlink. Re-provision it with the `install` command in section 4 and install
the `systemd-tmpfiles` entry to make it survive reboots.

### `Missing runtime Secret: service-connection-strings`

The three production Secrets are not all present. Run the provisioner in
section 6. If only `service-connection-strings` is missing (for example, after
migrating an environment that pre-existed the current Secret layout), generate
the 12 connection strings from the existing `db-credentials` Secret:

```sh
PWD=$(kubectl get secret db-credentials -n micro-cockpit \
  -o jsonpath='{.data.POSTGRES_PASSWORD}' | base64 -d)

kubectl get secret db-credentials -n micro-cockpit -o json \
  | jq -r '.data | to_entries[] | select(.key | endswith("_DB_PASSWORD")) | "\(.key)=\(.value)"' \
  | while IFS='=' read -r key enc; do
      role="${key%_DB_PASSWORD}_service"
      role="${role,,}"
      password="$(printf '%s' "$enc" | base64 -d)"
      prefix="${key%_DB_PASSWORD}"
      printf '%s_CONNECTION_STRING=Host=postgres;Database=trade_diary;Username=%s;Password=%s\n' \
        "$prefix" "$role" "$password"
    done > /tmp/connections.secret.env

kubectl create secret generic service-connection-strings \
  --namespace micro-cockpit \
  --from-env-file=/tmp/connections.secret.env \
  --dry-run=client -o yaml | kubectl apply -n micro-cockpit -f -

shred -u /tmp/connections.secret.env
```

### `Existing managed schemas have no migration history`

The database has schemas (usually from a `db-bootstrap` Job) but no migration
history. Run the baseline workflow in section 7. If application Deployments are
already running, scale them down first to avoid connection-slot starvation.

### `53300: remaining connection slots are reserved for roles with the SUPERUSER attribute`

See "Connection-slot starvation" in section 7. Scale application Deployments to
zero, run the baseline or migration, then scale back up.

### `image: .*:latest` or `does not contain one immutable image per application deployment`

The release helper refuses mutable tags and tags that are not full 40-character
SHAs. Pass the complete commit SHA as `--image-tag`. The pipeline derives this
automatically from `git rev-parse HEAD`.

### Ingress returns 503 / connection refused

Check that the target Service port matches the container port. The project
listens on `8080` everywhere; the `Ingress` routes to `frontend:8080`. If you
changed a port, update both `k8s/06-services.yaml` and the `Ingress`.

### TLS certificate never issues

- Confirm the Cloudflare API token has `Zone:DNS:Edit` on the correct zone.
- Confirm `cert-manager` is `Available` and the `ClusterIssuer` is `Ready`:
  `kubectl get clusterissuer letsencrypt-cloudflare -o wide`.
- Check `cert-manager` challenge logs:
  `kubectl logs -n cert-manager deploy/cert-manager | grep -i challenge`.
- DNS-01 needs the `_acme-challenge` TXT record to propagate; Cloudflare is
  usually instant but verify with `dig TXT _acme-challenge.trade-basic.com`.

### Pipeline builds but fails at `Deploy exact manifests`

Most deploy-step failures after a successful build are environment issues, not
code issues. Check, in order:

1. The four Forgejo inputs (section 9).
2. The lock directory (section 4).
3. The three runtime Secrets (section 6).
4. Database baseline / migration state (section 7).

The pipeline surfaces each of these with a distinct message; see the entries
above.
