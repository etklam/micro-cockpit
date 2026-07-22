# Completion audit — development plan §§19–22

Audit date: 2026-07-13. `PASS` means repository evidence or a recorded disposable-stack run directly proves the item. `PARTIAL` means implementation exists but a final acceptance check remains.

## Reproducible evidence

| Evidence | Command | Current result |
|---|---|---|
| All .NET projects | `dotnet build TradeDiary.slnx --no-restore -m:1 --disable-build-servers` | PASS, 0 errors; existing NU1903 Microsoft.OpenApi advisory warnings remain |
| Direct C# tests | `dotnet test TradeDiary.slnx --no-build -m:1 --disable-build-servers` | PASS, 21 tests across 7 test projects; includes atomic refresh rotation rollback/replay, WebApplicationFactory and Testcontainers PostgreSQL concurrency |
| Frontend production build | `npm --prefix frontend run build` | PASS |
| Generated Edge client | `npm --prefix frontend run api:verify` | PASS |
| Architecture boundaries | `./tests/verify-architecture.sh` | PASS, 13 services / 2 events |
| OpenAPI real contract | `npm --prefix frontend run api:verify` + `python scripts/validate-openapi.py` | PASS, 13 service docs generated from runtime DTOs + composed Edge doc (59 paths / 78 ops); all 14 validated by openapi-spec-validator |
| Migration ownership | `python3 scripts/audit-migrations.py` | PASS, 13 files / 13 schemas / 36 tables |
| Cross-language replacement | `python3 tests/verify-rewrite-contract.py` | PASS, independent Python Tool replacement |
| Domain golden tests | `tests/run-golden.sh` | PASS |
| Compose render | required DB envs + `docker compose config --quiet` | PASS |
| Full container deployment | isolated `docker compose -p micro-cockpit-qa ... up -d --build --wait` | PASS, fresh-volume stack healthy; Edge and frontend ready |
| Edge runtime smoke | `smoke-auth.sh`, `smoke-journal-idempotency.sh`, `smoke-jwt-ownership.sh`, degradation and role-isolation scripts through Edge | PASS, auth cookie rotation, byte-identical idempotency replay, JWT ownership, degradation and PostgreSQL role checks |
| Runtime DB isolation | `./tests/postgres-role-isolation.sh` | PASS |
| Dependency degradation | `./tests/service-unavailable-degradation.sh` | PASS, optional Reminder degrades and required Journal returns 503 |
| Backup and restore | custom dump restored into a disposable database | PASS, application tables verified and target removed |
| Release log secret scan | Compose logs checked for configured credentials, bearer tokens and API keys | PASS, no matches |
| Browser/device acceptance | Playwright at 1280×720 and 375×812 | PASS, login + 11 routes; no console/network/HTTP errors or horizontal overflow; keyboard focus advances |

The concurrent Journal idempotency harness now targets the public Edge routes. Production Compose still exposes only Edge and frontend; the verified disposable run used an isolated project and alternate local ports only because another local stack already occupied 5099/8080.

## Release phases

| Phase | Status | Evidence |
|---|---|---|
| 0 Architecture contract | PASS (repository) | Catalog, ownership manifest, API/event/view conventions, Compose, ADRs, runtime/migrator role setup and verifiers. |
| 1 Platform and Edge | PASS (repository) | Edge composition/proxy, correlation IDs, ProblemDetails paths, committed OpenAPI, generated client, dark/mobile shell. |
| 2 Identity | PASS (repository + smoke) | Local auth, refresh-family rotation, RSA/JWKS, persistent signing key, timezone/base currency, agent API keys, empty SSO. |
| 3 Journal | PASS (repository) | CRUD/transactions/Quick Note/soft-delete/events/idempotency implemented, with concurrent replay/conflict harness committed. |
| 4 Performance/calendar | PASS (repository + smoke) | Manual P/L, missing-is-null, aggregation, BFF and responsive calendar UI. |
| 5 Discipline/reminder | PASS (repository + smoke) | Deterministic/random modes, recurrence, locked worker, deletion event, concurrency smoke. |
| 6 v0.1 closure | PASS | Full UI, deployment/backup/security docs and Edge-based E2E harnesses exist; fresh-volume container, restore, degradation, log and browser/device checks pass. |
| 7 Stock Research | PASS (repository + smoke) | Stock-only directory, watchlist, mutable note, immutable timeline/corrections, UI. |
| 8 Market Data | PASS (repository + smoke) | Admin ingestion, provider health, daily bars, approved versioned view. |
| 9 Price Alert | PASS (repository + smoke) | Four conditions, worker, history, dismiss/reactivate, market-health fail-closed, UI. |
| 10 Rotation | PASS (build + golden) | Universes, snapshots, breadth, state, ranking/percentile, insufficient-data semantics, monitor UI. |
| 11 Partner and Agent | PASS (repository) | Human/agent links, per-side policies, scoped hashed API keys, short-lived agent JWTs, UI. |
| 12 Content, Tools, Operations | PASS (repository) | Publishing, four focused calculators with user-scoped presets/snapshots, audit/jobs/health service, Edge contracts. |

## Completion checklist

### Architecture

- PASS: every service has `SERVICE.md`, Dockerfile, health/readiness/version and committed OpenAPI.
- PASS: frontend calls Edge only and regenerates its client from Edge OpenAPI.
- PASS: no shared DbContext; static verifier finds no cross-service domain imports or cross-schema DML.
- PASS: event and published-view contracts are versioned.
- PASS: rewrite protocol is documented and an independent Python replacement passes the Tool contract.
- PASS: per-service LOGIN roles and separate migrator are wired into Compose; the final-stack negative privilege test passes.

### Product

- PASS: all named product areas and core mobile loop are implemented.
- PASS: no holdings, cost-basis, tax-lot, brokerage or portfolio-accounting engine exists.
- PASS: cross-user 404 behavior is covered in core and later-service smoke scripts.
- PASS: final Compose browser acceptance covers desktop/mobile login, all 11 navigable product pages, empty states, overflow, console/network failures and keyboard focus.

### Security and operations

- PASS: independent JWT validation, asymmetric signing/JWKS, hashed passwords/API keys/refresh tokens, atomic refresh rotation with rollback/reuse revocation, service-key separation, schema role declarations and published-view allow-list.
- PASS: final runtime privilege-negative test, container smoke, backup/restore, degradation and release log-secret review have recorded passing outputs.

## Release status

Controlled staging ready. The previously identified first-round blockers are fixed and verified: atomic refresh rotation, latest published-date Rotation scheduling, failed batch persistence, Edge-only E2E routing, runtime/OpenAPI auth alignment, required application secrets, forwarded-header trust, secure production cookies and byte-identical Journal idempotency replay.

No remaining release blockers were found for controlled staging. Public production deployment still requires the operator to configure trusted reverse-proxy addresses/networks and replace all template secrets.
