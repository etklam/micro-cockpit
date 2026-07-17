# Micro Cockpit

Diary-first investment decision journal with a React application, a typed Edge BFF, independently buildable .NET services, and schema-isolated PostgreSQL ownership.

## Documentation

| Read this | When you need to |
|---|---|
| [Run Micro Cockpit locally](docs/tutorial-getting-started.md) | Start the complete stack and exercise the first diary workflow |
| [Develop and verify changes](docs/how-to-development.md) | Run local development loops, tests, contract generation, and migrations |
| [System reference](docs/reference-system.md) | Look up processes, repository layout, routes, configuration, and health endpoints |
| [API and data reference](docs/reference-api-data.md) | Look up public routes, authentication, ProblemDetails, composition, ownership, and null rules |
| [Architecture](docs/explanation-architecture.md) | Understand the Edge boundary, session model, schema isolation, migration runner, and trade-offs |

Operational procedures remain in [Operations](docs/operations.md), [Database migrations](docs/database-migrations.md), and [Rollback](docs/rollback.md). Product boundaries are in [PRODUCT.md](PRODUCT.md), UI direction is in [DESIGN.md](DESIGN.md), and service ownership is summarized in [SERVICE_CATALOG.md](SERVICE_CATALOG.md).

## Run the production-like stack

All required secrets live in `.env` (git-ignored). Copy the template and edit every value
before exposing the stack — Compose fails fast if any of the 16 variables is unset.

```sh
cp .env.example .env      # then edit .env and replace every change-me-* value
docker compose up -d --build
docker compose ps
```

Open <http://localhost:8080>. The Edge API is at <http://localhost:5099>; PostgreSQL is bound
to localhost on `5433`. Backend services have no host ports — only Frontend and Edge are public.

Public registration is disabled by default. For local browser signup, set
`ALLOW_PUBLIC_REGISTRATION=true` in `.env`, restart Identity, then open `/register`.
When public registration remains false, gated registration requires
`X-Registration-Key: <LOCAL_REGISTRATION_KEY>` on `POST /api/auth/register` only
(Edge does not forward that header to any other route).

```sh
docker compose logs -f edge
docker compose down        # stops containers, keeps the data volume
```

## Operations

Backup, restore, reset, and signing-key handling (full commands in [docs/operations.md](docs/operations.md)):

- **Backup** — `pg_dump -Fc` of the `trade_diary` database (all schemas) to a file.
- **Restore** — `pg_restore --clean --if-exists` into the target database; verify application tables.
- **Destructive reset** — `docker compose down -v` removes the data volume; the next `up` re-runs migrations on an empty database.
- **Signing key backup** — Identity's RSA private key lives in the `identity-keys` volume (`/keys/signing-key.pem`). Back it up; losing it invalidates all outstanding refresh tokens and forces re-login. The JWT `kid` is derived from the public key, so it is stable across restarts and only changes on key rotation.
- **Database migrations** — Versioned, checksummed, forward-only migration and baseline procedures are in [docs/database-migrations.md](docs/database-migrations.md).

The system uses one shared PostgreSQL database and one deployment-time migration runner with an immutable ordered ledger. Services retain ownership of their schema design through migration metadata; runtime services never execute migrations, and the runner contains no shared business/domain code.

## Run for development

```sh
docker compose up -d --build --wait --wait-timeout 300
docker compose stop frontend
npm --prefix frontend ci
npm --prefix frontend run dev -- --host 127.0.0.1
```

This runs Vite against the composed Edge API on port `5099`. For backend service iteration, contract generation, focused tests, and the complete CI-equivalent command set, follow [Develop and verify changes](docs/how-to-development.md).

Internal APIs accept only Identity-issued Bearer JWTs or explicitly service-key protected machine calls; user ID headers are not trusted. Edge exposes `/health/live`, `/health/ready`, `/version`, and typed `/api/app/*` contracts.

## Boundaries

- The frontend calls only the Edge API.
- Each backend capability is an independently buildable service.
- No holdings, cost-basis, brokerage, or portfolio accounting engine.
- Compose keeps backend traffic on a private Docker network; only Frontend and
  Edge are publicly bound.
