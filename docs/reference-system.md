# System reference

This document describes the deployable processes, repository layout, frontend routes, runtime configuration, and operational entry points in Micro Cockpit. For design rationale, read [Architecture](explanation-architecture.md). For HTTP and data contracts, read [API and data reference](reference-api-data.md).

## Technology baseline

| Area | Technology | Repository evidence |
|---|---|---|
| Backend and Edge | ASP.NET Core on .NET 10 | `TradeDiary.slnx` and every `*.csproj` target `net10.0` |
| Database | PostgreSQL 17 | `compose.yaml` uses `postgres:17-alpine` |
| Database access | Npgsql 10 | Service and migrator project files |
| Frontend | React 19, TypeScript 6, Vite 8 | `frontend/package.json` |
| Routing and server state | React Router 7, TanStack Query 5 | `frontend/package.json` and `frontend/src/App.tsx` |
| Frontend tests | Vitest, Testing Library, user-event, MSW | `frontend/package.json` and `frontend/src/test/` |
| CI runtime | .NET 10, Node.js 22, Python 3 | `.github/workflows/ci.yml` |
| Deployment | Docker Compose and Kubernetes | `compose.yaml`, `k8s/`, `.forgejo/workflows/deploy.yml` |

## Repository layout

| Path | Purpose |
|---|---|
| `frontend/` | React application, generated Edge client, feature adapters, query hooks, and UI tests |
| `gateway/TradeDiary.EdgeApi/` | Browser-facing Edge API, authentication cookie boundary, typed BFF composition, and service proxy modules |
| `services/*-service/` | Independently buildable domain services and their local `SERVICE.md` notes |
| `platform/postgres/` | PostgreSQL image, roles, append-only migrations, legacy baseline fingerprint, and migration runner |
| `contracts/openapi/` | Committed service and Edge OpenAPI documents |
| `contracts/events/` | Versioned event envelope and event payload schemas |
| `contracts/schema-ownership.json` | Normative database ownership and cross-schema read policy |
| `scripts/` | Contract generation, migration validation, deployment, baseline, backup, and credential operations |
| `tests/` | .NET tests, architecture checks, safety checks, and disposable-stack smoke tests |
| `k8s/` | Namespace, PostgreSQL, services, frontend, Edge, and ingress manifests |

## Runtime topology

All application containers listen on port `8080` inside their network. Compose publishes only three host ports:

| Component | Host address | Exposure |
|---|---|---|
| Frontend | `http://localhost:8080` | Public; proxies `/api` to Edge |
| Edge API | `http://localhost:5099` | Public application API |
| PostgreSQL | `127.0.0.1:5433` | Host-local administration and development only |

Backend services have no host ports in Compose. The `backend` Docker network is marked internal. The frontend and Edge also join the `public` network.

## Services

| Process | Responsibility | Owned schema | Direct dependencies |
|---|---|---|---|
| `identity` | Users, credentials, JWTs, refresh-token families, agents, API keys | `identity` | PostgreSQL, RSA key volume |
| `journal` | Diaries, Quick Notes, transactions, structured diary reviews | `journal` | Identity metadata, Reminder event endpoint |
| `performance` | One manually entered daily P/L record per user and local date | `performance` | Identity metadata |
| `discipline` | User discipline statements and deterministic daily selection | `discipline` | Identity metadata |
| `reminder` | Diary alerts, recurrence, delivery attempts, event inbox/outbox | `reminder` | Identity metadata, Journal API |
| `stock-research` | Stocks, watchlists, notes, append-only timeline evidence | `stock_research` | Identity metadata |
| `market-data` | Symbols, provider runs, adjusted daily bars, published views | `market`, `market_data_public` | PostgreSQL; service-key ingestion |
| `price-alert` | Price alert definitions, evaluation, and trigger history | `price_alert` | Identity metadata, published market views |
| `rotation` | ETF universes, rotation snapshots, breadth, market state | `rotation` | Identity metadata, published market view |
| `partner` | Human/agent links and per-side sharing policy | `partner` | Identity metadata |
| `content` | Public educational posts and admin publishing | `content` | Identity metadata |
| `tool` | Financial calculators, user presets, saved calculation snapshots | `tool` | Identity metadata, Journal API for optional source validation |
| `operations` | Audit events, job requests, service-health history | `operations` | Identity metadata; service-key writes |
| `edge` | Public API, session cookie boundary, BFF composition | none | All application services |
| `frontend` | Browser application | none | Edge only |

The canonical ownership rules are in [`contracts/schema-ownership.json`](../contracts/schema-ownership.json). Service-specific invariants are documented in each `services/*/SERVICE.md` file and summarized in [`SERVICE_CATALOG.md`](../SERVICE_CATALOG.md).

## Edge module structure

`gateway/TradeDiary.EdgeApi/Program.cs` registers services and middleware, maps endpoint modules, and calls `app.Run()`. Endpoint modules are grouped by responsibility:

| Module | Public responsibility |
|---|---|
| `HealthEndpoints` | Liveness, readiness, version |
| `AuthenticationEndpoints` | Registration, login, refresh, logout, API-key exchange |
| `BootstrapEndpoints` | Authenticated application bootstrap |
| `CompositionEndpoints` | Dashboard, calendar, and stock-page BFF responses |
| `JournalEndpoints` | Diary, transaction, Quick Note, and review routes |
| `FeatureEndpoints` | Performance, discipline, and diary-alert routes |
| `ResearchEndpoints` | Stock research, market data, price alerts, and rotation |
| `PartnerEndpoints` | Partner lists, invitations, sharing policy, and privacy-safe compare composition |
| `AdminEndpoints` | Tools, content administration, operations administration |

`EdgeTransport` applies a per-request downstream timeout, propagates request cancellation and correlation IDs, forwards authorization and idempotency headers, and maps transport failures to safe ProblemDetails responses. `X-Registration-Key` is forwarded only when the Edge register route explicitly opts in for Identity registration.

## Frontend architecture

The frontend dependency direction is:

```text
Route page
  -> feature query or mutation hook
    -> feature API adapter
      -> generated Edge client
        -> Edge API
```

The access token exists only in module memory in `frontend/src/api.ts`. `AuthProvider` owns session restoration, authenticated/anonymous state, logout, and protected query-cache clearing. The generated transport performs one single-flight refresh and retries one failed request after a `401`.

### Routes

| Route | Screen |
|---|---|
| `/login` | Sign in |
| `/register` | Public sign-up when enabled |
| `/today` | Dashboard/today view |
| `/diary` | Diary list and recent review patterns |
| `/diary/:diaryId` | Diary, transactions, and optional structured review |
| `/calendar/:year/:month` | Calendar month, for example `/calendar/2026/07` |
| `/discipline` | Discipline statements |
| `/alerts` | Diary alerts |
| `/more` | Secondary navigation |
| `/watchlist` | Stock watchlist |
| `/price-alerts` | Price alerts |
| `/rotation` | Market rotation dashboard; selections live in URL query parameters |
| `/partners` | Partner and agent links |
| `/articles` | Published article list |
| `/articles/:slug` | Article detail |
| `/tools` | Financial calculators |

Unknown authenticated routes render the not-found state. Protected deep links restore the session before rendering or redirect to `/login`.

## Configuration

ASP.NET Core maps environment variables with double underscores to configuration keys. For example, `Services__Journal` maps to `Services:Journal`.

| Configuration | Consumer | Default or constraint |
|---|---|---|
| `ConnectionStrings__<Service>` | Stateful services | Required; one least-privilege database role per service |
| `Auth__MetadataAddress` | Edge and JWT-authenticated services | Identity OIDC metadata endpoint |
| `Jwt__Issuer` | Identity | Compose sets `trade-diary-identity` |
| `Jwt__Audience` | Identity | Compose sets `trade-diary-services` |
| `Jwt__PrivateKeyPath` | Identity | Compose sets `/keys/signing-key.pem` |
| `Auth__AllowPublicRegistration` | Identity | Code and Compose default `false`; set `true` intentionally for local browser signup |
| `Auth__LocalRegistrationKey` | Identity | Required only when public registration is disabled and registration should remain key-gated |
| `Internal__ServiceKey` | Journal, Reminder, Market Data, Price Alert, Operations | Required for internal machine calls |
| `Services__<Service>` | Edge | Downstream base address; local fallbacks use ports `5100` through `5112` |
| `Edge__DownstreamTimeoutSeconds` | Edge | Default `8`; clamped to `1..30` seconds |
| `ForwardedHeaders__KnownProxies` / `Proxy__TrustedProxies` | Edge | Empty by default; required before trusting `X-Forwarded-For` for client IP / rate-limit partitions |
| `ForwardedHeaders__KnownNetworks` / `Proxy__TrustedNetworks` | Edge | Empty by default; CIDR list of trusted reverse-proxy networks |
| `Workers__Reminder__Enabled` | Reminder | Default `true` |
| `Workers__Reminder__IntervalSeconds` | Reminder | Default `30` |
| `Workers__PriceAlert__Enabled` | Price Alert | Default `true` |
| `Workers__PriceAlert__IntervalSeconds` | Price Alert | Default `60` |
| `Workers__Rotation__Enabled` | Rotation | Default `true` |
| `Workers__Rotation__IntervalSeconds` | Rotation | Default `60` |
| `VITE_API_URL` | Frontend build | Empty by default; same-origin `/api` is preferred |

The complete Compose secret list is in [`.env.example`](../.env.example). Never commit a populated `.env` file.

## Health and version endpoints

Every process exposes:

- `GET /health/live`: process liveness.
- `GET /health/ready`: dependency/readiness state.
- `GET /version`: service name and version.

Edge reports `healthy`, `ready`, and version `0.1.0`. Service readiness may include database, published-view, provider, or worker-specific checks.

## Canonical generated artifacts

| Artifact | Source | Verification |
|---|---|---|
| Service OpenAPI documents | Runtime DTOs and endpoint metadata | `node scripts/generate-openapi.mjs --check` |
| Edge OpenAPI | Service documents plus typed Edge composition routes | `node scripts/compose-edge-openapi.mjs --check` |
| Frontend Edge client | `contracts/openapi/edge-api.openapi.json` | `node scripts/verify-edge-client.mjs` |
| Migration manifest | Exact migration bytes and SHA-256 values | `python3 scripts/validate-migrations.py` |

Do not edit `frontend/src/generated/edge.ts` manually.

## Related documentation

- [Getting started](tutorial-getting-started.md)
- [How to develop and verify changes](how-to-development.md)
- [API and data reference](reference-api-data.md)
- [Architecture](explanation-architecture.md)
- [Operations](operations.md)
- [Database migrations](database-migrations.md)
