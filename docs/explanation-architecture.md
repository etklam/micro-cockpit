# Architecture

Micro Cockpit is a diary-first investment decision journal. Its architecture keeps the user workflow centered on observations, decisions, transactions, daily P/L, and later review. Market and research capabilities support that loop; they do not become a portfolio accounting engine.

## The problem

The application combines user-owned journal data, calculated market facts, background alerting, public content, and administrative operations. A single browser-facing API must remain simple while backend ownership stays explicit. The system also needs safe releases across many schemas without letting each runtime service mutate database structure independently.

The main failure modes are:

- Browser code learns internal service addresses or downstream response shapes.
- Optional dashboard data turns a healthy core request into a total outage.
- A timeout or invalid downstream payload is mistaken for valid empty data.
- One service reads or writes another service's private tables.
- Missing financial values become zero and change their meaning.
- Authentication credentials leak into browser storage or URLs.
- Independently applied schema migrations race or drift across a shared database.

## The approach

```text
Browser
  |
  | same-origin HTTP, generated client
  v
Frontend (React Router + TanStack Query + AuthProvider)
  |
  | /api/* only
  v
Edge API
  |-- session cookie boundary
  |-- typed screen-oriented BFF composition
  |-- correlation, timeout, cancellation, safe errors
  |
  +--> Identity
  +--> Journal ------outbox event------> Reminder inbox
  +--> Performance
  +--> Discipline
  +--> Stock Research
  +--> Market Data --published views--> Price Alert / Rotation
  +--> Partner / Content / Tool / Operations
                    |
                    v
          shared PostgreSQL instance
          with schema and role isolation
```

### Edge-only browser boundary

The browser calls only Edge. Internal services expose `/internal/*` contracts and remain on the private network. This prevents frontend bundles from containing service URLs and gives the application one place to enforce authentication, correlation, transport policy, and public response types.

The trade-off is an extra network hop. The benefit is a stable application API that can compose screen-oriented responses without teaching the browser about service topology.

### Typed BFF composition

BFF means backend for frontend: Edge combines several service responses into one screen-shaped contract. Dashboard, calendar, stock page, bootstrap, and rotation responses use explicit DTOs.

Composition distinguishes three outcomes:

1. A required service failure fails the complete request.
2. An optional service failure marks its capability `unavailable`.
3. Valid domain absence stays `empty` or `null`.

Authentication failures are never degraded. A downstream `401` or `403` fails the whole request. Timeouts become `504`, unavailable services become `503`, and invalid typed payloads become `502`. This stops infrastructure failures from appearing as successful empty screens.

The trade-off is more Edge contract code and tests. The benefit is that UI behavior remains predictable under partial failure.

### Browser session boundary

Identity issues short-lived RS256 access tokens and rotating refresh tokens. Edge removes the refresh token from login and refresh responses, then stores it in the `td_refresh` cookie:

- `HttpOnly`
- `SameSite=Lax`
- secure in production/HTTPS-aware deployments
- path `/api/auth`
- maximum age 30 days

The frontend keeps the access token in memory only. It does not write the token to localStorage, sessionStorage, IndexedDB, a URL, or a JavaScript-readable cookie. A reload calls `POST /api/auth/refresh`; a failed restore or logout clears the token and protected query cache.

The trade-off is that every full reload needs a refresh request. The benefit is a smaller browser credential exposure surface.

### Service-owned schemas in one PostgreSQL database

Each stateful service owns one private schema, except Market Data, which owns both `market` and the published `market_data_public` contract schema. Runtime roles receive DML privileges only for their owned objects. Cross-service reads use HTTP or named, versioned published views.

This design keeps local deployment and transactional operations manageable while enforcing service boundaries at the database privilege layer. It is not database-per-service isolation: a database outage can affect the full system, and schema changes still require coordinated ordering.

### One migration runner and immutable ledger

The database migrator is deployment infrastructure, not shared domain code. It verifies the manifest and exact SQL checksums, takes an advisory lock, executes each pending migration and history row in one transaction, and rejects drift or destructive automatic DDL.

Runtime services never migrate. Fresh startup follows:

```text
PostgreSQL healthy
  -> role bootstrap
  -> migration runner
  -> role finalization
  -> application services
```

Legacy adoption is explicitly versioned through migration `0013`. Baseline verifies the legacy fingerprint and records only `0001..0013` as baseline rows; migrations `0014` and later remain pending and execute normally.

The trade-off is a centrally ordered migration stream. The benefit is one deterministic release gate across a shared database, immutable history, and safe expand-first deployment.

### Generated contracts

Services generate OpenAPI from runtime DTOs. The Edge document composes service operations and typed BFF routes. The frontend client is generated from the committed Edge document. CI regenerates all three layers and fails on drift.

This makes contract changes deliberate, but generated artifacts add review noise when the public surface changes. Manual generated-client edits are forbidden because they would be overwritten and would hide source drift.

### Events and background work

Journal publishes `DiaryDeleted.v1` through a transactional outbox. Reminder consumes it through an inbox keyed by event ID. The versioned event envelope includes correlation and causation identifiers.

Reminder, Price Alert, and Rotation run background workers. Claiming uses database concurrency controls and uniqueness/idempotency rules so multiple instances do not duplicate an occurrence or calculation. Worker failure is logged but does not crash readiness by itself.

The trade-off is eventual consistency for event-driven cleanup and scheduled calculations. The benefit is that user requests do not wait for every downstream side effect.

## Domain boundaries that remain intentional

- Transactions are diary records, not holdings or cost-basis inputs.
- Daily P/L is manually entered and missing P/L remains missing.
- Structured diary review is optional and does not infer process quality from P/L.
- Rotation uses only backend calculations; the frontend does not calculate RSI, moving averages, ranks, percentiles, or signals.
- Market Data accepts authenticated external ingestion jobs; it does not contain a fake or embedded external provider.
- Tool calculations are pure and create no portfolio records. Tool Service persists only user-scoped presets and reconstructable calculation snapshots, with server-recalculated output.

## Trade-offs summary

| Choice | Gain | Cost |
|---|---|---|
| Edge-only browser API | Stable public boundary and centralized transport policy | Additional hop and Edge composition code |
| Shared PostgreSQL with schema roles | Simple deployment and enforceable ownership | Shared failure domain and ordered migrations |
| Memory-only access token | Reduced browser persistence exposure | Refresh call after reload |
| Typed screen BFFs | Clear partial-failure and null semantics | More DTOs and contract tests |
| Generated OpenAPI/client | Detectable contract drift | Generated artifact maintenance |
| Outbox/inbox events | Reliable eventual side effects | Eventual consistency and worker operations |
| Expand-first migrations | Safe rolling compatibility | Destructive cleanup requires a later phase |

## Related documentation

- [System reference](reference-system.md)
- [API and data reference](reference-api-data.md)
- [Database migration architecture decision](decisions/ADR-database-migration-runner.md)
- [Schema-boundary ADR](adr/0001-shared-postgres-schema-boundaries.md)
- [No cross-schema writes ADR](adr/0002-no-cross-schema-writes.md)
- [Event rewrite protocol ADR](adr/0003-event-rewrite-protocol.md)
