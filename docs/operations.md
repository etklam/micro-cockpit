# Operations

## Local stack

Copy `.env.example` to `.env`, replace every placeholder, then start the active
product:

```sh
docker compose up --build -d
docker compose ps
```

The frontend is served at `http://localhost:8080`, Edge at
`http://localhost:5099`, and PostgreSQL at `127.0.0.1:5433`. Identity,
Journal, Market Data, and Tool remain on the private Docker network.

```sh
docker compose logs -f edge
docker compose down
```

Use `docker compose down -v` only when intentionally deleting local database
data.

## Backup and restore

```sh
docker compose exec -T postgres \
  pg_dump -U trade_diary -d trade_diary -Fc > trade-diary.dump

docker compose stop frontend edge tool market-data journal identity
docker compose exec -T postgres \
  pg_restore -U trade_diary -d trade_diary --clean --if-exists < trade-diary.dump
docker compose start identity journal market-data tool edge frontend
```

## Upgrade

```sh
docker compose build --pull
docker compose up -d
```

The migrator validates the immutable manifest and applies pending migrations
before application services start.

## Kubernetes release

First-time K3s, ingress, DNS, runtime Secret, and baseline setup is documented
in [deploy-k3s.md](deploy-k3s.md). Normal releases call
`scripts/deploy-k8s-release.sh` with one full commit SHA for Identity, Journal,
Market Data, Tool, Edge, and Frontend. Runtime credentials stay in the
`db-credentials`, `service-connection-strings`, and `app-secrets` Secrets.

Release, baseline, and credential rotation share the namespace operation lock.
Bootstrap and rotation are explicit operator actions:

```sh
scripts/provision-k8s-secrets.sh \
  --namespace micro-cockpit \
  --env-file /secure/path/production.secret.env \
  --confirm-create-or-replace

scripts/rotate-k8s-credentials.sh \
  --context EXPECTED_CONTEXT \
  --namespace micro-cockpit \
  --backup-confirmed BACKUP_REFERENCE
```

Never run either command as part of a normal release. Use a dedicated deploy
identity and a separately controlled operator identity.

Identity persists its RSA signing key in the `identity-keys` volume at
`/keys/signing-key.pem`; back up and restrict that volume like any other
authentication secret.

## Runtime model

- Browser access tokens remain in memory. Refresh tokens use an
  `HttpOnly`, `SameSite=Lax`, secure-in-production cookie scoped to `/api/auth`.
- Public registration is disabled unless explicitly enabled and remains
  protected by the registration key and rate limiting.
- Market Data ingestion is an external authenticated job. Only completed runs
  publish raw and adjusted Daily Close views.
- OpenAPI is generated from the four active services and composed at Edge.
- The release smoke proves the Human → Observation → Expectation → Review →
  Watchlist → Daily Close → Agent User → Access Grant → incremental deletion
  path and verifies retired routes return `404`.
