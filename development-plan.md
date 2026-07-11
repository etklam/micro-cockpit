# DEVELOPMENT_PLAN.md — Replaceable Shared-Database Services

Revision status: rewritten from the reconciled Diary-first Trade Journal plan.

---

## 1. Project Overview

This project is a fresh rebuild of the existing trade diary product.

It is:

```text
- Not an old-data migration project.
- Not a direct Nuxt / Vue / Prisma rewrite.
- Not a traditional modular monolith.
- Not a fully isolated database-per-service microservice platform.
```

It is a **shared-database, independently replaceable service architecture** designed for the vibe-coding era.

Primary objective:

```text
Each backend service must be small enough to understand and regenerate,
but complete enough to own one coherent business capability.

A service may later be rewritten in C#, Go, Java, Rust, or another stack
without rewriting the whole product.
```

New stack:

```text
Frontend: React + TypeScript + Vite
Edge API / BFF: ASP.NET Core initially
Backend services: ASP.NET Core initially, independently replaceable later
Database: One PostgreSQL instance
Database isolation: One PostgreSQL schema per service
ORM: EF Core initially; each replacement service may choose its own data access layer
API contracts: OpenAPI
Frontend API client: Generated from the Edge API / BFF OpenAPI
Internal event contracts: Versioned JSON envelopes
Deployment: One container per service
Repository: Monorepo
```

Product direction:

```text
Diary-first Trade Journal
+ Stock Research Layer
+ Daily P/L Calendar
+ ETF Market Rotation Tool
```

Core product loop:

```text
Observe market
→ Write Diary / Quick Note
→ Record optional Transaction
→ Record Daily P/L
→ Receive Diary Alert
→ Review Discipline
→ Improve decision process
```

---

## 2. Why This Architecture

The purpose of splitting the backend is not organisational scale.

The purpose is **rewriteability**.

A service should be replaceable because:

```text
- Its API contract is explicit.
- Its database schema ownership is explicit.
- It does not import another service's domain code.
- It does not use another service's EF entities.
- It can run and test independently.
- It can be replaced behind the Edge API without changing the frontend.
- Its implementation language is not part of the product contract.
```

This architecture deliberately accepts one shared PostgreSQL instance to reduce:

```text
- VPS resource usage
- operational cost
- backup complexity
- network latency
- local development complexity
- distributed transaction requirements
```

However, sharing one PostgreSQL instance does **not** mean all services may freely access all tables.

The system uses:

```text
Shared PostgreSQL instance
≠ shared table ownership
≠ shared DbContext
≠ unrestricted cross-service SQL
```

---

## 3. Hard Constraints

```text
- No old data migration.
- No compatibility with the old database schema.
- Do not copy the old Nuxt / Prisma architecture.
- Backend APIs must be app-first, not CRUD-only wrappers.
- Do not expose ORM entities in API responses.
- Use DTOs and OpenAPI-generated clients.
- Local login only initially.
- Reserve SSO structure but do not implement SSO yet.
- Dark mode first.
- Mobile is first-class.
- Diary remains the product centre.
- Daily P/L Calendar is allowed.
- Full portfolio accounting is not allowed.
- No holdings, cost-basis, tax-lot, or brokerage engine.
- Stock research and ETF research remain separate.
- Diary Alert and Price Alert remain separate.
- Every service owns its schema, migrations, API, tests, and container.
- A service must not write another service's schema.
- A service must not import another service's application or domain project.
- Cross-user resources return 404, not 403.
- Feature tests ship in the same phase as the feature.
```

---

## 4. Architecture Classification

This design should be described as:

```text
Shared-Database Replaceable Services
```

or:

```text
Service-oriented modular platform with schema isolation
```

It should not be presented as strict database-per-service microservices.

The system has microservice-style deployment and contracts, while intentionally sharing one PostgreSQL instance.

This distinction matters because the design avoids pretending that the services are fully infrastructure-independent.

---

## 5. High-Level Architecture

```text
                    ┌─────────────────────┐
                    │ React Web / PWA     │
                    └──────────┬──────────┘
                               │
                               ▼
                    ┌─────────────────────┐
                    │ Edge API / App BFF  │
                    │ Auth validation     │
                    │ Screen composition  │
                    │ API routing         │
                    └─────┬─────┬─────────┘
                          │     │
             ┌────────────┘     └─────────────────────┐
             ▼                                        ▼
    ┌─────────────────┐                    ┌─────────────────┐
    │ Core services   │                    │ Research/tools  │
    │ Identity        │                    │ Stock Research  │
    │ Journal         │                    │ Market Data     │
    │ Performance     │                    │ Price Alert     │
    │ Discipline      │                    │ Rotation        │
    │ Reminder        │                    │ Content/Tools   │
    └────────┬────────┘                    └────────┬────────┘
             │                                      │
             └────────────────┬─────────────────────┘
                              ▼
                  ┌────────────────────────┐
                  │ One PostgreSQL instance│
                  │ schema per service     │
                  └────────────────────────┘
```

The frontend communicates with the Edge API only.

The Edge API may:

```text
- Route resource requests to an owning service.
- Compose screen-ready responses from multiple services.
- Apply request correlation IDs.
- Normalize ProblemDetails responses.
- Expose one frontend-facing OpenAPI contract.
```

The Edge API must not:

```text
- Own domain tables.
- implement Diary, P/L, alerts, research, or market calculations.
- become a hidden monolith.
- directly mutate service-owned schemas.
```

---

## 6. Repository Structure

```text
trade-diary/
  frontend/
    src/
      app/
      pages/
      widgets/
      features/
      entities/
      shared/
      routes/
      styles/

  gateway/
    TradeDiary.EdgeApi/
    TradeDiary.EdgeApi.Contracts/
    TradeDiary.EdgeApi.Tests/

  services/
    identity-service/
      src/
      tests/
      migrations/
      openapi/
      Dockerfile
      SERVICE.md

    journal-service/
      src/
      tests/
      migrations/
      openapi/
      Dockerfile
      SERVICE.md

    performance-service/
    discipline-service/
    reminder-service/
    stock-research-service/
    market-data-service/
    price-alert-service/
    rotation-service/
    partner-service/
    content-service/
    tool-service/
    operations-service/

  contracts/
    events/
    published-db-views/
    test-fixtures/

  platform/
    compose/
    postgres/
    reverse-proxy/
    observability/
    scripts/

  docs/
    CONTEXT.md
    DESIGN.md
    DEVELOPMENT_PLAN.md
    SERVICE_CATALOG.md
    architecture/
    api/
    database/
    operations/
    decisions/
    rewrite-guides/
```

Each service folder is a self-contained application boundary.

A service may use multiple projects internally, but a small service should avoid unnecessary ceremony.

Recommended initial service layout:

```text
<Service>.Api
<Service>.Application
<Service>.Infrastructure
<Service>.Tests
```

Only introduce a separate Domain project when the service has meaningful domain logic.

Do not automatically create:

```text
Domain
Application
Infrastructure
Contracts
SharedKernel
Common
Abstractions
```

for every tiny service.

The service structure should reflect actual complexity.

---

## 7. Database Strategy

## 7.1 One PostgreSQL Instance

Initial deployment uses one PostgreSQL database:

```text
trade_diary
```

Suggested schemas:

```text
platform
identity
journal
performance
discipline
reminder
stock_research
market_data
price_alert
rotation
partner
content
tool
operations
```

Each service receives:

```text
- One schema owner role
- One runtime role
- One migration role
- Access only to its owned schema
- Explicit read access to approved published views
```

Example roles:

```text
journal_owner
journal_runtime
journal_migrator

performance_owner
performance_runtime
performance_migrator
```

## 7.2 Schema Ownership Rules

```text
- Identity Service writes only identity.*
- Journal Service writes only journal.*
- Performance Service writes only performance.*
- Reminder Service writes only reminder.*
- No service may run migrations against another schema.
- No shared EF Core DbContext exists.
- No global migration project exists.
- Every service has its own migration history table in its own schema.
```

Example EF Core migration history:

```text
journal.__ef_migrations_history
performance.__ef_migrations_history
identity.__ef_migrations_history
```

A future Go or Java rewrite may stop using EF Core while keeping the same service-owned schema and API contract.

## 7.3 Cross-Service Foreign Keys

Default rule:

```text
Do not create physical foreign keys across service-owned schemas.
```

Instead, store external identifiers such as:

```text
user_id
diary_id
stock_id
```

and validate them through:

```text
- JWT claims
- owning-service API calls
- versioned events
- application-level consistency checks
```

Reason:

```text
A cross-schema foreign key makes database ownership ambiguous and makes a service rewrite harder.
```

Exception:

```text
A cross-service foreign key may be introduced only through an Architecture Decision Record
when the integrity benefit clearly exceeds the replacement cost.
```

No exception should be required for v0.1.

## 7.4 Published Database Views

Because all services share PostgreSQL, selected high-volume read models may be published as stable read-only views.

Example:

```text
market_data_public.latest_daily_prices_v1
market_data_public.adjusted_daily_bars_v1
journal_public.diary_day_counts_v1
```

Rules:

```text
- Consumers receive SELECT permission on published views only.
- Consumers never read another service's base tables.
- View names are versioned.
- Breaking view changes create a new version.
- A published view is a contract, not an implementation shortcut.
- Services must remain able to replace a view dependency with an API or event later.
```

Use published views only where HTTP fan-out would create obvious performance or batch-processing problems.

## 7.5 Shared Platform Tables

The `platform` schema contains infrastructure-only records:

```text
platform.service_registry
platform.schema_versions
platform.event_subscriptions
```

It must not contain product domain entities.

Avoid a generic shared table such as:

```text
platform.entities
platform.settings
platform.references
platform.metadata
```

---

## 8. Service Contract Rules

Each service exposes:

```text
- OpenAPI document
- Health endpoint
- Readiness endpoint
- Version endpoint
- ProblemDetails errors
- Correlation ID support
- Structured logs
```

Required endpoints:

```http
GET /health/live
GET /health/ready
GET /version
```

API rules:

```text
- IDs are lower-case UUID strings.
- Enum values are strings.
- Timestamps are UTC ISO-8601.
- User-facing dates use YYYY-MM-DD.
- Pagination uses { items, nextCursor } where needed.
- Mutation endpoints support idempotency where retries can duplicate data.
- Every service validates the authenticated user independently.
- The gateway is not the only authorization layer.
```

The public frontend contract belongs to the Edge API.

Internal service APIs have separate OpenAPI documents.

This allows an internal service to be rewritten while the frontend contract remains unchanged.

---

## 9. Internal Communication

Use three communication modes.

## 9.1 Synchronous HTTP

Use when the caller needs an immediate answer.

Examples:

```text
- Reminder Service validates that a Diary exists.
- Edge API requests selected-day Diary details.
- Partner Service verifies a share relationship.
```

Rules:

```text
- Short timeout.
- No unbounded retry.
- Propagate correlation ID.
- Use generated internal clients.
- Do not share DTO assemblies directly between services.
```

## 9.2 Versioned Events

Use when another service can update asynchronously.

Examples:

```text
DiaryCreated.v1
DiaryDeleted.v1
DailyPerformanceUpdated.v1
StockPriceUpdated.v1
PriceAlertTriggered.v1
PartnerLinkAccepted.v1
```

Initial implementation may use a PostgreSQL outbox and polling consumer.

No external broker is required for v0.1.

Event envelope:

```json
{
  "eventId": "uuid",
  "eventType": "DiaryCreated.v1",
  "occurredAt": "2026-07-11T12:00:00Z",
  "producer": "journal-service",
  "correlationId": "uuid",
  "payload": {}
}
```

Each service owns:

```text
<schema>.outbox_events
<schema>.inbox_events
```

Rules:

```text
- Outbox insert occurs in the same transaction as the domain change.
- Consumers use inbox deduplication.
- Event payloads contain IDs and necessary immutable facts only.
- Events are not remote ORM entities.
- Breaking event changes require a new event version.
```

## 9.3 Published Read Views

Use for batch-heavy and read-heavy workflows, especially:

```text
- Market Rotation calculations over daily bars.
- Price Alert scans.
- Operational reporting.
```

They are not the default communication mechanism.

---

## 10. Service Catalogue

## 10.1 Edge API / App BFF

Owns no domain schema.

Responsibilities:

```text
- Frontend-facing API
- Screen-ready response composition
- Authentication entry routing
- Service routing
- Correlation IDs
- Frontend OpenAPI
- Response normalization
```

Example app APIs:

```http
GET /api/app/dashboard
GET /api/app/diary-editor-context
GET /api/app/diary-detail/{id}
GET /api/app/calendar
GET /api/app/calendar/day
GET /api/app/discipline-center
GET /api/app/diary-alert-center
GET /api/app/watchlist-overview
GET /api/app/stock-research/{symbol}
GET /api/app/tools/market-rotation-monitor
```

Rewrite target:

```text
The BFF can be replaced independently as long as the frontend OpenAPI remains compatible.
```

## 10.2 Identity Service

Schema:

```text
identity
```

Owns:

```text
users
user_credentials
user_external_logins
auth_sessions
refresh_tokens
api_keys
```

Responsibilities:

```text
- Registration
- Login
- Refresh-token rotation
- Logout
- Current-user profile
- Role, account type, status
- User timezone
- User base currency
- SSO provider discovery
```

Initial APIs:

```http
POST /internal/auth/register
POST /internal/auth/login
POST /internal/auth/refresh
POST /internal/auth/logout
GET  /internal/auth/me
GET  /internal/auth/sso/providers
```

SSO initial response:

```json
{
  "enabledProviders": []
}
```

Rules:

```text
- Local login only initially.
- Public self-signup is disabled until email verification and password recovery exist.
- JWT validation metadata is available to every service.
- Services authorize ownership from the token subject.
```

## 10.3 Journal Service

Schema:

```text
journal
```

Owns:

```text
diaries
diary_tags
diary_stock_mentions
transactions
```

Capabilities:

```text
- Diary CRUD
- Transaction CRUD inside Diary
- Quick Note create/append workflow
- Diary timeline
- Diary detail
```

Why these stay together:

```text
- Transaction is a child of Diary.
- Quick Note mutates Diary.
- They require the same ownership and transaction boundary.
- Splitting them would create more distributed coordination than rewrite value.
```

Resource APIs:

```http
GET    /internal/diaries
POST   /internal/diaries
GET    /internal/diaries/{id}
PUT    /internal/diaries/{id}
DELETE /internal/diaries/{id}

POST   /internal/diaries/{diaryId}/transactions
PUT    /internal/diaries/{diaryId}/transactions/{transactionId}
DELETE /internal/diaries/{diaryId}/transactions/{transactionId}

POST   /internal/quick-note
GET    /internal/diary-timeline
GET    /internal/diary-day-summary
```

Rules preserved from the original plan:

```text
- Multiple Diaries may exist on one local date.
- Diary may exist without Transaction.
- Transaction must belong to one Diary.
- Transactions do not create holdings.
- Transactions do not calculate cost basis.
- Quick Note never guesses a same-day Diary.
- Missing targetDiaryId means create a new Diary.
- Cross-user and missing IDs both return 404.
```

## 10.4 Performance Service

Schema:

```text
performance
```

Owns:

```text
daily_performances
```

Capabilities:

```text
- Manual Daily P/L input
- Monthly P/L summary
- Win/loss/flat-day aggregation
- Best/worst day
- Percentage coverage calculation
```

APIs:

```http
GET    /internal/daily-performances
PUT    /internal/daily-performances/{date}
DELETE /internal/daily-performances/{date}
GET    /internal/performance/month-summary
GET    /internal/performance/day/{date}
```

Rules:

```text
- Daily P/L belongs to user + local date.
- It may exist without Diary.
- pnl_amount is required for a recorded day.
- pnl_percent is server-derived only when capital_base exists.
- Missing P/L is not zero.
- One base currency only in v0.1.
- No holdings or portfolio accounting.
```

The service does not build the full calendar screen.

The Edge API composes Performance data with Journal, Discipline, and Reminder data.

## 10.5 Discipline Service

Schema:

```text
discipline
```

Owns:

```text
disciplines
```

Capabilities:

```text
- Discipline CRUD
- Reorder
- Random browse
- Deterministic Today's Discipline
```

APIs:

```http
GET    /internal/disciplines
POST   /internal/disciplines
PUT    /internal/disciplines/{id}
DELETE /internal/disciplines/{id}
POST   /internal/disciplines/reorder
GET    /internal/disciplines/random
GET    /internal/disciplines/today
```

Rules:

```text
- No DisciplineCheck entity.
- Today's Discipline is deterministic for user + local date.
- Random browse does not affect Today's Discipline.
```

## 10.6 Reminder Service

Schema:

```text
reminder
```

Owns:

```text
diary_alerts
reminder_delivery_attempts
outbox_events
inbox_events
```

Capabilities:

```text
- Diary Alert CRUD
- Weekday recurrence
- In-app trigger state
- Worker claims
- Dismiss/expire
```

APIs:

```http
GET    /internal/diary-alerts
POST   /internal/diary-alerts
PUT    /internal/diary-alerts/{id}
DELETE /internal/diary-alerts/{id}
POST   /internal/diary-alerts/{id}/dismiss
GET    /internal/diary-alerts/day-summary
```

Rules:

```text
- Reminder stores diary_id but owns no Diary data.
- On create, it verifies the Diary through Journal Service.
- Journal deletion events expire linked reminders.
- v0.1 delivery is in-app only.
- Worker uses FOR UPDATE SKIP LOCKED.
- Weekends are skipped.
- Diary Alert remains separate from Price Alert.
```

## 10.7 Stock Research Service

Schema:

```text
stock_research
```

Owns:

```text
stocks
watchlist_items
stock_notes
stock_timeline_records
```

Capabilities:

```text
- Stock directory
- Watchlist
- Mutable current Stock Note
- Immutable Stock Timeline
```

Rules:

```text
- Watchlist contains stocks only.
- ETF research does not enter this service.
- Stock Timeline records are immutable evidence.
- Corrections create new records.
```

This service ships after v0.1.

## 10.8 Market Data Service

Schema:

```text
market_data
```

Owns:

```text
daily_prices
quote_cache
provider_runs
provider_failures
outbox_events
```

Capabilities:

```text
- Provider integration
- Daily bar persistence
- Quote refresh
- Provider health
- Canonical symbol metadata required by market workflows
```

Published views:

```text
market_data_public.latest_quotes_v1
market_data_public.adjusted_daily_bars_v1
```

Rules:

```text
- Market Data contains facts, not user research.
- It does not own Watchlist.
- It does not own Price Alerts.
- It does not calculate Market Rotation UI payloads.
```

## 10.9 Price Alert Service

Schema:

```text
price_alert
```

Owns:

```text
price_alerts
price_alert_evaluations
price_alert_triggers
```

Capabilities:

```text
- Above/below alerts
- Percent-change alerts
- Moving-average crossing alerts
- Trigger/dismiss/reactivate workflow
```

Data access:

```text
Reads approved market-data views or consumes price events.
Does not read market_data base tables.
```

Rules:

```text
- Cannot activate while Market Data is unhealthy.
- Triggered alerts do not automatically re-arm.
- Price Alert remains separate from Diary Alert.
```

## 10.10 Rotation Service

Schema:

```text
rotation
```

Owns:

```text
market_rotation_universes
market_rotation_universe_symbols
market_rotation_snapshots
sector_breadth_snapshots
market_state_snapshots
batch_runs
```

Capabilities:

```text
- ETF universe management
- Rotation snapshots
- Sector breadth
- Market state
- Ranking and percentile calculations
- Market Rotation Monitor payload
```

Data access:

```text
Reads market_data_public.adjusted_daily_bars_v1.
```

Rules:

```text
- ETFs remain under Tools.
- Stock and ETF research do not merge.
- Percentiles and ranks partition by universe, date, and rank scope.
- Unknown data remains null / insufficient_data.
- Formula changes require golden-test updates and explicit versioning.
```

## 10.11 Partner Service

Schema:

```text
partner
```

Owns:

```text
partner_links
partner_share_policies
```

Capabilities:

```text
- Human partner links
- AI Agent Partner links
- Independent per-side sharing controls
- Share authorization decisions
```

Rules:

```text
- AI Agent Partner is a normal User account.
- Partner Service does not copy shared resources.
- Shared content remains owned by its author.
- Transactions are private by default.
```

The BFF asks Partner Service for authorization and then asks the owning service for a sanitized read model.

## 10.12 Content Service

Schema:

```text
content
```

Owns:

```text
posts
post_tags
```

Capabilities:

```text
- Public educational posts
- Admin publishing workflow
```

It remains separate from Diary.

## 10.13 Tool Service

Schema:

```text
tool
```

Owns only tool configuration or saved tool runs when needed.

Capabilities may include:

```text
- Position sizing
- Risk/reward
- FIRE calculator
- Relative value
- Seasonality
```

Prefer pure stateless calculation endpoints.

Do not combine Market Rotation into a generic calculation codebase when its persistence and batch jobs justify a separate Rotation Service.

## 10.14 Operations Service

Schema:

```text
operations
```

Owns:

```text
audit_events
job_registry
service_health_history
admin_run_requests
```

Capabilities:

```text
- Cross-service audit projection
- Admin operational overview
- Manual batch requests
- Service health history
```

Domain services publish audit-relevant events through their outboxes.

Operations Service is deferred until after the core product loop is stable.

---

## 11. Frontend Plan

Stack:

```text
React
TypeScript
Vite
TanStack Query
React Router
React Hook Form
Zod
Tailwind CSS
TanStack Table
Recharts
Orval
```

Rules:

```text
- Frontend calls the Edge API only.
- No service URL is embedded in React code.
- No raw fetch inside components.
- API client is generated from Edge API OpenAPI.
- Server state uses TanStack Query.
- Forms use React Hook Form + Zod.
- Dark mode is default.
- Mobile Quick Note and Calendar are first-class.
- Feature folders follow product capability, not backend service names blindly.
```

Suggested frontend areas:

```text
Dashboard
Diary
Quick Note
Calendar
Discipline
Diary Alerts
Watchlist
Stock Research
Price Alerts
Partners
Articles
Tools
Admin
```

The frontend must not become responsible for cross-service domain assembly.

The Edge API returns screen-ready payloads.

---

## 12. App Composition Rules

A screen may require multiple services.

Example: Calendar month screen.

The Edge API composes:

```text
Performance Service:
- Daily P/L
- Monthly summary

Journal Service:
- Diary counts by local date
- Transaction counts by transaction local date
- Mood summary

Reminder Service:
- Alert markers

Discipline Service:
- Selected-day discipline suggestion
```

Response:

```text
GET /api/app/calendar?year=2026&month=7
```

The BFF may call services in parallel.

Failure policy:

```text
- Required data failure returns a normal error.
- Optional widget failure returns partial data plus capability status.
- Missing Daily P/L is domain absence, not service failure.
- Service timeout is not converted to zero or empty success silently.
```

The BFF must not query four schemas directly to build the response.

---

## 13. Authentication and Authorization

Identity Service issues access tokens.

Every service validates:

```text
issuer
audience
signature
expiry
subject
role
account_type
status version where applicable
```

Ownership rules:

```text
- User-owned resource lookup starts with current user ID.
- Query shape is equivalent to (user_id, resource_id).
- Cross-user and missing resource both return 404.
- Gateway authorization does not replace service authorization.
- Partner-shared reads use explicit share decisions.
- Agent-owned records remain owned by the agent account.
```

Internal service calls use:

```text
- User token propagation when acting for a user.
- Service credentials when executing system jobs.
- Explicit execution context in logs and audit events.
```

Do not create a universal superuser database role for runtime services.

---

## 14. Observability

Every request includes:

```text
correlation_id
trace_id
user_id when authenticated
service_name
service_version
endpoint
status_code
duration_ms
```

Every service exposes:

```text
liveness
readiness
build version
database connectivity state
dependency state
```

Logs are structured JSON.

Initial deployment may use:

```text
OpenTelemetry
Prometheus-compatible metrics
Grafana or a lighter compatible dashboard
central log collection when operationally justified
```

Do not block v0.1 on a full observability stack.

Minimum v0.1 requirement:

```text
- Structured logs
- Correlation IDs
- Health endpoints
- Worker execution records
- Error visibility
```

---

## 15. Testing Strategy

Each service has its own test suite.

Required categories:

```text
- Domain/unit tests where logic exists
- PostgreSQL integration tests
- API contract tests
- Ownership/IDOR negative tests
- Timezone tests where local dates exist
- Migration tests
- Container smoke tests
```

Cross-service tests:

```text
- Edge API composition tests
- Consumer-driven contract tests
- Event schema tests
- Published-view contract tests
- End-to-end happy path
- Service-unavailable degradation tests
```

Golden tests are required for:

```text
- Monthly P/L aggregation
- Alert recurrence
- Market Rotation calculations
- Timezone day boundaries
```

A service is not replaceable merely because it has OpenAPI.

It is replaceable only when the replacement can run the same contract and golden tests.

---

## 16. Service Rewrite Protocol

This is the core architectural requirement.

When rewriting a service:

### Step 1 — Freeze Contracts

Freeze:

```text
- Frontend-facing BFF contract
- Internal service OpenAPI
- Owned database schema contract
- Published views
- Produced and consumed event versions
- Golden fixtures
```

### Step 2 — Create a Parallel Implementation

Example:

```text
services/journal-service/
services/journal-service-next/
```

The new implementation must not import old service code.

It may reuse:

```text
- OpenAPI files
- JSON schemas
- test fixtures
- container conventions
```

It must not reuse:

```text
- old domain assemblies
- old EF entities
- old application services
- hidden shared business libraries
```

### Step 3 — Test Against a Disposable Database

Run:

```text
- migration compatibility tests
- API contract tests
- ownership tests
- golden tests
- load smoke tests
```

The replacement must work against a database created from committed migrations.

### Step 4 — Shadow Read

For read endpoints:

```text
- Send production-like requests to both old and new implementations.
- Compare normalized responses.
- Ignore expected non-deterministic metadata.
- Record mismatch rate.
```

Do not dual-write by default.

### Step 5 — Single-Writer Cutover

At any time, only one implementation writes the service-owned schema.

Cutover:

```text
- Stop old writer.
- Apply backward-compatible migration if required.
- Start replacement writer.
- Switch gateway routing.
- Keep old container available for rollback when schema remains backward-compatible.
```

### Step 6 — Remove Old Implementation

Remove only after:

```text
- Contract tests pass.
- Error rate is stable.
- No rollback is required.
- Documentation and SERVICE_CATALOG are updated.
```

Definition of a successful rewrite:

```text
- Frontend unchanged.
- Other domain services unchanged.
- Same service-owned data remains usable.
- No cross-service migration required.
- Gateway route switch is the main deployment change.
```

---

## 17. Rules That Preserve Rewriteability

Do:

```text
- Keep services small and cohesive.
- Keep OpenAPI committed.
- Generate clients.
- Use schema-per-service.
- Version events and views.
- Keep migration ownership local.
- Use contract fixtures.
- Keep BFF composition outside domain services.
- Prefer explicit duplication of tiny DTOs over shared business libraries.
- Store implementation-independent values in the database.
```

Avoid:

```text
- Shared EF entities
- Shared DbContext
- Shared generic repository
- Shared Domain project
- Direct cross-schema writes
- Unversioned database views
- Frontend calls to service-specific URLs
- One giant Worker controlling every domain
- One global migration pipeline
- Distributed transaction coordinator
- Service-to-service calls for every small read
- Splitting entities that require one transaction boundary
```

---

## 18. Service Size Guidance

A service is too large when:

```text
- It has unrelated release reasons.
- Its tables have separate ownership and privacy rules.
- Rewriting one capability requires understanding many unrelated features.
- It has multiple independent worker lifecycles.
```

A service is too small when:

```text
- Most requests require a synchronous call to another service.
- One business transaction spans both services.
- It owns only a child entity with no independent lifecycle.
- Its API is merely a proxy around another service.
```

Therefore:

```text
Diary + Transaction + Quick Note stay together.
Performance is separate.
Discipline is separate.
Diary Reminder is separate.
Stock Note + Timeline + Watchlist stay together.
Market Data is separate.
Price Alert is separate.
Market Rotation is separate.
```

---

## 19. Release Plan

## Phase 0 — Architecture Contract

Deliver:

```text
- Monorepo structure
- SERVICE_CATALOG.md
- Schema ownership map
- API conventions
- Event envelope
- Published-view rules
- Docker Compose
- PostgreSQL role setup
- Correlation ID convention
- ADR: shared database decision
- ADR: no cross-schema writes
- ADR: service rewrite protocol
```

Exit criteria:

```text
- A template service can start.
- It owns one schema.
- It exposes health/version endpoints.
- It can be replaced without frontend knowledge.
```

## Phase 1 — Platform and Edge API

Deliver:

```text
- Edge API skeleton
- Request routing
- ProblemDetails normalization
- Frontend OpenAPI generation
- React app shell
- Dark mode
- Mobile navigation
- Local compose environment
```

Do not implement product dashboards yet.

## Phase 2 — Identity Service

Deliver:

```text
- Registration for local/self-hosted use
- Login
- Refresh-token rotation
- Logout
- Current user
- Timezone
- Base currency
- Role/account type/status
- Empty SSO provider endpoint
```

Tests ship in this phase.

## Phase 3 — Journal Service

Deliver:

```text
- Diary CRUD
- Transaction CRUD
- Quick Note create/append
- Diary timeline
- Ownership checks
- Timezone-local dates
- Soft delete
- Idempotency
```

Frontend:

```text
- Diary list
- Diary editor
- Quick Note
- Transaction editor
```

## Phase 4 — Performance Service and Calendar Composition

Deliver:

```text
- Manual Daily P/L
- Monthly aggregation
- Percentage coverage rules
- Calendar BFF composition with Journal data
- Desktop month calendar
- Mobile calendar list/week strip
```

No portfolio accounting.

## Phase 5 — Discipline and Reminder Services

Deliver:

```text
- Discipline CRUD/reorder/random/today
- Diary Alert CRUD
- Week/month weekday recurrence
- In-app trigger state
- Reminder worker
- Journal deletion event handling
- Dashboard and calendar markers
```

## Phase 6 — v0.1 Product Closure

Deliver:

```text
- Diary-first dashboard
- Full mobile core loop
- Cross-service E2E tests
- Backup/restore test
- Service failure behaviour
- Deployment documentation
- Security review
```

v0.1 includes:

```text
Identity
Edge API
Journal
Performance
Discipline
Reminder
React frontend
```

v0.1 excludes:

```text
Market Data
Price Alert
Stock Research
Partner/Agent
Market Rotation
Posts
Extra tools
Admin/Ops UI
```

## Phase 7 — Stock Research Service

Deliver:

```text
- Stock directory
- Watchlist
- Stock Note
- Stock Timeline
- Immutable correction model
```

## Phase 8 — Market Data Service

Deliver:

```text
- Provider abstraction
- Daily prices
- Quote cache
- Provider runs
- Health state
- Published read views
```

## Phase 9 — Price Alert Service

Deliver:

```text
- Alert conditions
- Evaluation worker
- Trigger history
- BFF alert centre
```

Market Data health must exist first.

## Phase 10 — Rotation Service

Deliver:

```text
- ETF universes
- Snapshots
- Breadth
- Market state
- Ranking
- Golden tests
- Market Rotation Monitor
```

## Phase 11 — Partner and Agent

Deliver:

```text
- Partner links
- Independent sharing flags
- AI Agent Partner accounts
- API keys and scopes
- Read-only shared projections
- Idempotent agent writes
```

## Phase 12 — Content, Tools, Operations

Deliver only after usage justifies them:

```text
- Posts
- Stateless calculators
- Operational audit projection
- Admin batch requests
- Service health overview
```

---

## 20. Deployment Plan

Initial production deployment may use Docker Compose.

Containers:

```text
frontend
edge-api
identity-service
journal-service
performance-service
discipline-service
reminder-service
postgres
```

Later containers are added by phase.

Rules:

```text
- One container per service.
- One PostgreSQL instance.
- Internal service network is private.
- Only frontend and Edge API are publicly exposed.
- Each service uses its own database runtime role.
- Migrations run as explicit deployment jobs, not automatically from every replica.
- Database backups cover the full PostgreSQL instance.
- Restore tests verify all schemas together.
```

A shared database means backup and restore occur at database level.

Service-level export tools may be added later, but they do not replace full-database backup.

---

## 21. Security Checklist

```text
[ ] Every service validates JWT independently
[ ] Runtime database roles cannot write other schemas
[ ] Migration roles are separate from runtime roles
[ ] No cross-user resource enumeration
[ ] Partner sharing is allow-list based
[ ] Refresh token reuse revokes its session family
[ ] Cookie endpoints validate CSRF
[ ] API keys are hashed and scoped
[ ] Service credentials are distinct from user tokens
[ ] Logs do not contain passwords, raw tokens, or API keys
[ ] Published views expose only approved columns
[ ] Admin/manual jobs use the same locked worker path
```

---

## 22. Completion Checklist

### Architecture

```text
[ ] Every service has a SERVICE.md
[ ] Every service has one schema owner
[ ] No shared DbContext exists
[ ] No service writes another service schema
[ ] Internal OpenAPI contracts are committed
[ ] Frontend calls only Edge API
[ ] Event contracts are versioned
[ ] Published views are versioned
[ ] Rewrite protocol is documented and tested on at least one service
```

### v0.1 Product

```text
[ ] Local auth works
[ ] SSO provider endpoint returns an empty provider list
[ ] User timezone exists
[ ] User base currency exists
[ ] Dark mode works
[ ] Mobile navigation works
[ ] Diary CRUD works
[ ] Multiple Diaries per date work
[ ] Quick Note create/append is deterministic
[ ] Transaction belongs to Diary
[ ] Daily P/L manual input works
[ ] Missing P/L is not zero
[ ] Calendar composes Journal and Performance data
[ ] Discipline random and today modes are separate
[ ] Diary Alert recurrence works
[ ] Concurrent workers do not double-trigger reminders
[ ] No holdings engine exists
[ ] No cost-basis engine exists
[ ] No brokerage integration exists
```

### Rewriteability

```text
[ ] A replacement service can use another language
[ ] Replacement does not import old business code
[ ] Replacement passes the same contract tests
[ ] Replacement can use the same owned schema
[ ] Gateway can switch routing without frontend changes
[ ] Only one writer is active during cutover
[ ] Rollback procedure is documented
```

---

## 23. Final Architecture Decision

The target is not maximum microservice purity.

The target is:

```text
Maximum practical replaceability
with minimum operational overhead.
```

The final design principle is:

> Split by independent business capability and rewrite boundary, not by table count.

The shared PostgreSQL instance is an intentional operational choice.

Schema ownership, API contracts, published views, versioned events, and contract tests are what make later service rewrites possible.

Without those boundaries, multiple containers would only create a distributed monolith.
