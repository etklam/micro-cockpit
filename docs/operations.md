# Operations

## Start and stop

All required secrets are listed in `.env.example` (git-ignored once copied). Set non-default
secrets before exposing the stack:

```sh
cp .env.example .env      # edit .env and replace every change-me-* value
docker compose up -d --build
docker compose ps
```

(You may instead `export` each variable inline — the full list is in `.env.example`.)

Use independently generated values from the deployment secret store. Compose
has no database-password defaults: missing secrets fail configuration instead
of silently starting with production credentials from source control.

Database startup uses three one-shot services after PostgreSQL becomes healthy:
`db-role-bootstrap`, `db-migrate`, and `db-role-finalize`. Applications wait for
all three. The migrator owns managed schemas and history; runtime roles receive
only their documented CRUD and cross-schema view grants:

```sh
docker compose run --rm db-migrate status
./tests/postgres-role-isolation.sh
```

See [database-migrations.md](database-migrations.md) for canonical migration creation, existing-volume baseline, failure handling, and production adoption.

The shared PostgreSQL database has one deployment-time runner and one ordered migration ledger. Individual services own their schema design and migration metadata, but runtime service processes never apply migrations. Before first production adoption, run the documented read-only Kubernetes database status command; baseline and failed-Job retry remain explicit operator actions.

`frontend` is served at `http://localhost:8080` and proxies `/api` to Edge.
Edge is also bound at `http://localhost:5099`. PostgreSQL is reachable only
from the host at `127.0.0.1:5433`; the other services stay on the private
Docker network.

```sh
docker compose logs -f edge
docker compose down
```

Use `docker compose down -v` only when intentionally deleting all database
data.

## Backup

The custom-format dump includes every service schema:

```sh
docker compose exec -T postgres \
  pg_dump -U trade_diary -d trade_diary -Fc > trade-diary.dump
```

## Restore

Stop application writes first. Restore replaces matching database objects:

```sh
docker compose stop frontend edge reminder discipline performance journal identity
docker compose exec -T postgres \
  pg_restore -U trade_diary -d trade_diary --clean --if-exists < trade-diary.dump
docker compose start identity journal performance discipline reminder edge frontend
```

## Upgrade

```sh
docker compose build --pull
docker compose up -d
```

## Kubernetes release deployment

Forgejo builds every application image with the full checked-out commit SHA and deploys that same SHA through `scripts/deploy-k8s-release.sh`. The helper renders a temporary Kustomize overlay, so tracked manifests are not edited. Base application manifests deliberately contain `REQUIRED_IMAGE_TAG` and cannot be deployed through the generic manifest helper. The release helper rejects mutable or shortened tags, applies all 15 application images together, records the SHA in `app.kubernetes.io/version` and `micro-cockpit/deployed-sha`, and then verifies live images, annotations, availability, and the absence of `CrashLoopBackOff`.

Production runtime credentials have one authoritative source: the current `db-credentials`, `service-connection-strings`, and `app-secrets` Kubernetes Secrets in the production namespace. Forgejo Actions stores deployment infrastructure credentials only: registry credentials, the SSH key, the pinned known-hosts entry, and deployment host/user configuration. A normal release never receives, transfers, creates, patches, or replaces application runtime credentials. While holding the namespace operation lock, it verifies that all required Secrets and keys are present, applies non-secret infrastructure, and deploys the immutable application images.

Configure `DEPLOY_HOST` and `DEPLOY_USER` as Forgejo variables. Configure `DEPLOY_SSH_KEY` and `DEPLOY_KNOWN_HOSTS` as Forgejo secrets. Obtain the known-hosts entry independently from the deployment host administrator and verify its fingerprint out of band; never populate it with deployment-time `ssh-keyscan`. Missing host, user, or known-hosts data fails closed. Every SSH, SCP, and cleanup path uses strict host checking, the pinned file, and `IdentitiesOnly=yes`.

Use a dedicated deployment account rather than `root`. Its Kubernetes permissions should be restricted to reading the required Secret metadata/keys, applying project non-secret manifests, restarting project Deployments, and reading rollout and Pod status. Normal deployment does not need permission to create, patch, or replace Secrets. Bootstrap and rotation should use a separately controlled operator identity. The deployment account also needs access to the namespace-scoped operation lock file on the host.

Forgejo serializes production deployments with a concurrency group. The remote host additionally uses `scripts/with-k8s-operation-lock.sh`, which takes `/run/lock/micro-cockpit/micro-cockpit-<namespace>.operation.lock` before runtime Secret verification and holds it through database upgrade, manifest rendering, image update, rollout, and final verification. Credential rotation and explicit baseline use the same lock for their entire operations.

Provision the lock directory outside CI with a restricted shared operations group; deployment and rotation identities must belong to that group:

```sh
sudo install -d -o root -g micro-cockpit-deploy-ops -m 2770 /run/lock/micro-cockpit
```

The directory must be a real directory, group-writable, setgid, and not world-writable. `MICRO_COCKPIT_LOCK_DIR` may select another pre-provisioned path. CI never creates this privileged directory.

## Kubernetes runtime Secret bootstrap

Bootstrap, rotation, and release are separate operations:

- Bootstrap creates the three production runtime Secrets for the first time as an explicit operator action.
- Rotation updates PostgreSQL roles and those Secrets together during a maintenance window under the shared namespace lock.
- Release deployment changes application images and non-secret manifests without changing runtime Secrets.

Bootstrap requires protected operator-supplied input and explicit confirmation:

```sh
scripts/provision-k8s-secrets.sh \
  --namespace micro-cockpit \
  --env-file /secure/path/production.secret.env \
  --confirm-create-or-replace
```

This command creates production runtime credentials. Do not run it during a normal application release. If any target Secret already exists, the command fails without making changes. `--replace-existing` permits replacement only as a deliberate operator-controlled update; verify the database/runtime coordination procedure before using it. The tool never generates production credentials.

## Kubernetes credential rotation

Run rotation only in a maintenance window after creating a verified backup and confirming the exact Kubernetes context:

```sh
scripts/rotate-k8s-credentials.sh \
  --context EXPECTED_CONTEXT \
  --namespace micro-cockpit \
  --backup-confirmed BACKUP_REFERENCE
```

Run rotation on the same deployment host used by Forgejo so both operations contend on the same lock filesystem. The public rotation command validates the context and backup reference, acquires the shared namespace operation lock, and then runs the complete rotation inside it. The script captures all three current Secrets in a mode-0700 temporary directory, verifies every current PostgreSQL role login, and generates new credentials. Role changes use a fixed role allowlist and protected `psql` stdin. After PostgreSQL commits, any later failure activates rollback. Journal, Reminder, Market Data, Price Alert, and Operations are stopped together while the shared internal key changes, preventing mixed-key event delivery.

Successful rotation requires all new database logins to succeed, every old database login to fail, the old internal key to receive `403`, and the new key to pass authentication and reach a non-mutating `400` validation response. Temporary credentials and probe headers are removed on every exit.

Rollback restores PostgreSQL role passwords first, restores the exact captured data for `db-credentials`, `service-connection-strings`, and `app-secrets`, restores replicas, performs recovery rollouts, and verifies old runtime credentials. If automated recovery reports an emergency, keep the maintenance window active: restore role passwords from protected incident material, restore all three Secrets, restart affected Deployments, and verify old logins before reopening traffic. Never paste credential material into commands, tickets, or logs.

Identity stores its RSA signing key in the `identity-keys` volume at
`/keys/signing-key.pem`, so ordinary container replacement does not invalidate
access tokens. Back up that volume and restrict access to it like any other
authentication secret.

## Architecture notes

### Authentication and session flow
The access token (15 min, RS256) is held in browser memory only. The refresh token never reaches
JavaScript: on login Edge extracts it from Identity's response, stores it in an
`HttpOnly`/`SameSite=Lax`/`Secure`(HTTPS) cookie scoped to `/api/auth`, and returns only
`{ accessToken, expiresAt }`. Browser signup first calls register, then uses the normal login path
for the session cookie. Reload restores the session by calling `POST /api/auth/refresh`
(Edge reads the cookie, forwards it to Identity, rotates the cookie). On a `401` the frontend
transport single-flight-refreshes once and retries; refresh failure ends the session. Logout calls
`POST /api/auth/logout` (Identity revokes the refresh family) and clears the cookie. Reusing an
already-used/revoked refresh token revokes the entire family.

### Stable JWT key id
`kid` is a SHA-256 thumbprint of the RSA **public** key (SubjectPublicKeyInfo), not a random value.
The persisted key therefore yields the same `kid` across Identity restarts, so unexpired access
tokens stay valid. Only a key rotation changes the `kid`.

### Background workers
Reminder, Price Alert, and Rotation each run a `BackgroundService` (no manual `/internal/worker/run`
needed). Poll interval and enable/disable are config-driven (`Workers:<Service>:IntervalSeconds`,
`Workers:<Service>:Enabled`, both with defaults, so Compose needs no new variables). Rotation also
uses `Workers:Rotation:RunAtUtc` (default `02:00`) as its daily universe schedule. They keep the
`FOR UPDATE SKIP LOCKED` claim logic (multi-instance safe), log each run under a correlation
run-id, and one failed run never stops the worker or fails readiness. Reminder delivery goes
through `IReminderDeliveryChannel` (currently `InAppReminderDeliveryChannel`, which records the
in-app delivery attempt — explicitly not an email/push stub). Price Alert fails closed when the
market provider is unhealthy and never re-triggers the same alert/trading-date. Rotation reuses the
idempotent `batch_runs` calculate and treats `insufficient_data` as a normal outcome.

### OpenAPI contract
OpenAPI is generated from real DTOs, not regex. Each service emits `/openapi.json` via
`Microsoft.AspNetCore.OpenApi`; `scripts/generate-openapi.mjs` runs each service and writes the 13
service documents; `scripts/compose-edge-openapi.mjs` composes the Edge public document from them,
and `scripts/validate-openapi.py` runs the standard `openapi-spec-validator` over all 14 documents
(the regex verifier was removed). The frontend client is generated from the Edge document
(`scripts/generate-edge-client.mjs`). `npm --prefix frontend run api:verify` fails if any committed
document or the client has drifted from the runtime/composed output.

### Testing strategy
Production logic is tested directly in C# (unit + `WebApplicationFactory` endpoint + Testcontainers
PostgreSQL integration). The legacy shell/Python smokes remain as disposable-stack runtime checks.
See `TradeDiary.*.Tests` projects under each service and the CI workflow.

### Market data ingestion
Market Data has no background fetcher and no fake provider. Bar ingestion is an **external job's**
responsibility: an authenticated loader drives `PUT /internal/admin/symbols/{raw}` →
`POST /internal/admin/provider-runs` → `PUT .../bars` → `POST .../complete`; only completed runs
publish into the cross-service `market_data_public` views that Price Alert and Rotation read.
