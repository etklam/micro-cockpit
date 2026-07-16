# How to develop and verify changes

This guide shows how to iterate on the frontend or backend, update contracts safely, and run the same checks used by CI.

## Prerequisites

- .NET SDK 10.
- Node.js 22 and npm.
- Python 3.
- Docker with Docker Compose v2.
- `openapi-spec-validator==0.7.2` when validating OpenAPI locally.
- A populated, git-ignored `.env` for integrated runtime work.

Install frontend dependencies exactly from the lockfile:

```sh
npm --prefix frontend ci
```

Install the OpenAPI validator if needed:

```sh
python -m pip install openapi-spec-validator==0.7.2
```

## Run the fastest static development loop

Restore and build .NET once:

```sh
dotnet restore TradeDiary.slnx
dotnet build TradeDiary.slnx --no-restore -m:1 --disable-build-servers
```

The serialized `-m:1` build matches CI and avoids local MSBuild contention across the solution.

Run backend tests without rebuilding:

```sh
dotnet test TradeDiary.slnx --no-build -m:1 --disable-build-servers
```

Run the frontend checks:

```sh
npm --prefix frontend run lint
npm --prefix frontend run build
npm --prefix frontend run test
```

## Run frontend development against the integrated Edge API

Start the complete stack, then replace only the containerized frontend with Vite:

```sh
docker compose up -d --build --wait --wait-timeout 300
docker compose stop frontend
npm --prefix frontend run dev -- --host 127.0.0.1
```

Open the Vite URL printed by the command. `frontend/vite.config.ts` proxies `/api` to the composed Edge API at `http://127.0.0.1:5099`. Browser code still uses same-origin paths and never learns internal service URLs.

When finished:

```sh
docker compose down
```

## Rebuild one backend capability

For a service-level change, rebuild the service and Edge while leaving the rest of the stack running. For example:

```sh
docker compose up -d --build journal edge
docker compose ps journal edge
```

Run the relevant focused test project before the full suite:

```sh
dotnet test tests/TradeDiary.Journal.Tests/TradeDiary.Journal.Tests.csproj
dotnet test tests/TradeDiary.EdgeApi.Tests/TradeDiary.EdgeApi.Tests.csproj
```

Replace the project paths with the owning service test project. Some test projects use Testcontainers and require a working Docker daemon.

## Add or change an Edge API operation

1. Change the owning service endpoint and explicit request/response DTOs.
2. Add or update service tests for validation, authorization, ownership, nulls, and failure behavior.
3. Map the public route in the matching `gateway/TradeDiary.EdgeApi/Endpoints/` module.
4. Use an explicit Edge DTO for composed responses. Do not expose database entities, anonymous response objects, `JsonNode`, `JsonObject`, or downstream raw payloads.
5. Add Edge tests for downstream timeout, invalid payload, authorization, and optional/required behavior where composition is involved.
6. Regenerate contracts:

   ```sh
   npm --prefix frontend run api:generate
   ```

7. Review changes under `contracts/openapi/` and `frontend/src/generated/edge.ts`. Do not hand-edit generated output.
8. Update the feature API adapter and query/mutation hook. Components must not import generated transport internals directly.
9. Verify drift:

   ```sh
   npm --prefix frontend run api:verify
   python scripts/validate-openapi.py
   ```

## Add or change a frontend screen

Keep the dependency direction:

```text
Page -> query/mutation hook -> feature API adapter -> generated Edge client
```

For server state:

1. Add a stable query key containing every request parameter that changes the response.
2. Put network calls in `frontend/src/features/api.ts`.
3. Put caching, invalidation, and mutation behavior in `frontend/src/features/queries.ts`.
4. Use route path or query state for navigation that must survive refresh and browser history.
5. Invalidate only affected queries after a mutation.
6. Add MSW-backed tests for loading, valid empty, unavailable, authorization, deep-link, and navigation behavior.

Raw `fetch` calls, internal service URLs, duplicate handwritten transport types, and frontend copies of backend market formulas are not accepted.

## Add a database migration

Read [Database migrations](database-migrations.md) before editing.

The short workflow is:

1. Add the next contiguous `NNNN_description.sql` file under `platform/postgres/migrations/`.
2. Add the required `migration-id`, `owner`, and `description` metadata lines.
3. Use expand-first, backward-compatible DDL.
4. Append the filename and exact SHA-256 to `manifest.json`.
5. Never edit an existing migration.
6. Run:

   ```sh
   python3 scripts/validate-migrations.py
   python3 scripts/audit-migrations.py
   python3 scripts/validate-migration-append-only.py --base-ref BASE_COMMIT
   ./tests/verify-migration-safety.sh
   ```

The baseline fingerprint represents the schema through migration `0013`; later migrations must remain outside that fingerprint.

## Run the complete verification set

Use the CI-equivalent build and test commands:

```sh
dotnet restore TradeDiary.slnx
dotnet build TradeDiary.slnx --no-restore -m:1 --disable-build-servers
dotnet test TradeDiary.slnx --no-build -m:1 --disable-build-servers

npm --prefix frontend ci
npm --prefix frontend run lint
npm --prefix frontend run build
npm --prefix frontend run test
npm --prefix frontend run api:verify

python scripts/validate-openapi.py
python3 scripts/validate-migrations.py
python3 scripts/audit-migrations.py

./tests/verify-architecture.sh
./tests/verify-secret-handling.sh
./tests/verify-deployment-safety.sh
./tests/verify-rotation-safety.sh
./tests/verify-runtime-secret-source.sh
./tests/verify-k8s-operation-lock.sh
./tests/verify-migration-safety.sh
```

For a change that adds a migration, also run the append-only validator against the immutable base commit selected for the review.

## Run disposable-stack smoke tests

The manual E2E workflow in `.github/workflows/e2e.yml` starts the full Compose stack with ephemeral secrets and runs authentication, idempotency, ownership, release-path, degradation, and database-role checks.

To inspect individual runtime scripts, see `tests/smoke-*.sh`, `tests/service-unavailable-degradation.sh`, and `tests/postgres-role-isolation.sh`. Run them only against a disposable local stack with the required environment variables.

## Verification

Before handing off a change:

```sh
git diff --check
git status --short
```

Confirm that:

- OpenAPI and generated client have no drift.
- No populated secret or connection string is staged.
- No historical migration changed.
- Frontend code calls Edge only.
- Missing domain values remain null rather than invented zeroes.
- Authorization failures do not become partial success.

## Troubleshooting

### `dotnet test` cannot open a local socket

`WebApplicationFactory` and test-host communication need loopback sockets. Run the command in an environment that permits local socket binding.

### A solution build appears to stop after one project

Stop stale build servers and rerun the serialized CI command:

```sh
dotnet build-server shutdown
dotnet build TradeDiary.slnx --no-restore -m:1 --disable-build-servers
```

Also stop locally running service processes that may hold build outputs.

### OpenAPI verification reports drift

Do not patch the generated client. Run `npm --prefix frontend run api:generate`, inspect the source DTO/endpoint change that caused the diff, and commit all intentional generated artifacts together.

### Testcontainers tests are skipped or fail to start

Verify Docker first:

```sh
docker info
docker ps
```

The database migrator, Identity, and Journal integration tests require disposable PostgreSQL containers.

### A Compose service cannot reach another service

Use Compose service names and container port `8080` inside the `backend` network. Do not add host ports as a shortcut. Only frontend, Edge, and host-local PostgreSQL are intentionally published.

## Related documentation

- [Getting started](tutorial-getting-started.md)
- [System reference](reference-system.md)
- [API and data reference](reference-api-data.md)
- [Architecture](explanation-architecture.md)
- [Security review checklist](security-review-checklist.md)
