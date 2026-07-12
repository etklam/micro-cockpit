# Completion audit — development plan §§19–22

Audit date: 2026-07-12. `PASS` means current repository evidence directly proves the item. `PARTIAL` means implementation exists but the final environment-dependent release check has not been recorded.

## Reproducible evidence

| Evidence | Command | Current result |
|---|---|---|
| All .NET projects | `dotnet build TradeDiary.slnx --no-restore -m:1 --disable-build-servers` | PASS, 0 warnings/errors |
| Frontend production build | `npm --prefix frontend run build` | PASS |
| Generated Edge client | `npm --prefix frontend run api:verify` | PASS |
| Architecture boundaries | `./tests/verify-architecture.sh` | PASS, 13 services / 2 events |
| OpenAPI/source parity | `python3 scripts/tests/verify-openapi.py` | PASS, 14 documents / 218 operations |
| Migration ownership | `python3 scripts/audit-migrations.py` | PASS, 13 files / 13 schemas / 36 tables |
| Cross-language replacement | `python3 tests/verify-rewrite-contract.py` | PASS, independent Python Tool replacement |
| Domain golden tests | `tests/run-golden.sh` | PASS |
| Compose render | required DB envs + `docker compose config --quiet` | PASS |

The following release checks have runnable scripts but their latest execution is not recorded because local Docker process execution was denied after the tool usage limit was reached:

- `scripts/container-smoke.sh`
- `tests/postgres-role-isolation.sh`
- `scripts/verify-backup-restore.sh`
- `tests/service-unavailable-degradation.sh`
- `tests/smoke-journal-idempotency.sh`

## Release phases

| Phase | Status | Evidence |
|---|---|---|
| 0 Architecture contract | PASS (repository) | Catalog, ownership manifest, API/event/view conventions, Compose, ADRs, runtime/migrator role setup and verifiers. |
| 1 Platform and Edge | PASS (repository) | Edge composition/proxy, correlation IDs, ProblemDetails paths, committed OpenAPI, generated client, dark/mobile shell. |
| 2 Identity | PASS (repository + smoke) | Local auth, refresh-family rotation, RSA/JWKS, persistent signing key, timezone/base currency, agent API keys, empty SSO. |
| 3 Journal | PARTIAL | CRUD/transactions/Quick Note/soft-delete/events/idempotency implemented. New concurrent idempotency smoke awaits recorded Docker run. |
| 4 Performance/calendar | PASS (repository + smoke) | Manual P/L, missing-is-null, aggregation, BFF and responsive calendar UI. |
| 5 Discipline/reminder | PASS (repository + smoke) | Deterministic/random modes, recurrence, locked worker, deletion event, concurrency smoke. |
| 6 v0.1 closure | PARTIAL | Full UI, deployment/backup/security docs and E2E harnesses exist; container/restore/degradation/browser acceptance outputs remain unrecorded. |
| 7 Stock Research | PASS (repository + smoke) | Stock-only directory, watchlist, mutable note, immutable timeline/corrections, UI. |
| 8 Market Data | PASS (repository + smoke) | Admin ingestion, provider health, daily bars, approved versioned view. |
| 9 Price Alert | PASS (repository + smoke) | Four conditions, worker, history, dismiss/reactivate, market-health fail-closed, UI. |
| 10 Rotation | PASS (build + golden) | Universes, snapshots, breadth, state, ranking/percentile, insufficient-data semantics, monitor UI. |
| 11 Partner and Agent | PASS (repository) | Human/agent links, per-side policies, scoped hashed API keys, short-lived agent JWTs, UI. |
| 12 Content, Tools, Operations | PASS (repository) | Publishing, five stateless calculators, audit/jobs/health service, Edge contracts. |

## Completion checklist

### Architecture

- PASS: every service has `SERVICE.md`, Dockerfile, health/readiness/version and committed OpenAPI.
- PASS: frontend calls Edge only and regenerates its client from Edge OpenAPI.
- PASS: no shared DbContext; static verifier finds no cross-service domain imports or cross-schema DML.
- PASS: event and published-view contracts are versioned.
- PASS: rewrite protocol is documented and an independent Python replacement passes the Tool contract.
- PARTIAL: per-service LOGIN roles and separate migrator are wired into Compose; the negative privilege test has not been rerun in the final stack.

### Product

- PASS: all named product areas and core mobile loop are implemented.
- PASS: no holdings, cost-basis, tax-lot, brokerage or portfolio-accounting engine exists.
- PASS: cross-user 404 behavior is covered in core and later-service smoke scripts.
- PARTIAL: browser/device visual and keyboard acceptance has not been recorded against the final Compose stack.

### Security and operations

- PASS: independent JWT validation, asymmetric signing/JWKS, hashed passwords/API keys/refresh tokens, refresh reuse revocation, service-key separation, schema role declarations and published-view allow-list.
- PARTIAL: final runtime privilege-negative test, container smoke, backup/restore, degradation run and release log-secret review still require recorded outputs.

## Remaining release blockers

1. Run the five environment-dependent scripts listed above against a disposable final Compose stack and retain their output.
2. Run final browser/mobile accessibility acceptance against that stack.
3. Review a release log sample for password/token/API-key leakage.

Until these checks pass, repository implementation is complete but release completion remains unproven.
