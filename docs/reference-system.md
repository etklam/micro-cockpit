# System reference

## Runtime

| Process | Configuration |
|---|---|
| Identity | `ConnectionStrings__Identity`, JWT issuer/audience/key path, registration controls |
| Journal | `ConnectionStrings__Journal`, Identity metadata, Identity and Market Data URLs, internal key |
| Market Data | `ConnectionStrings__MarketData`, internal key |
| Tool | `ConnectionStrings__Tool`, Identity metadata |
| Edge | Identity metadata plus Identity, Journal, Market Data, and Tool URLs |

Compose publishes frontend `8080`, Edge `5099`, and host-local PostgreSQL `5433`. Private services use port `8080` only on the backend network.

## Frontend routes

Public: `/`, `/login`, `/register`, `/tools`.
Authenticated: `/today`, `/today/observations`, `/review`, `/watchlist`, `/calendar/:year/:month`, `/tools`, `/settings`.

## Generated artifacts

- Service OpenAPI: `node scripts/generate-openapi.mjs`
- Edge OpenAPI: `node scripts/compose-edge-openapi.mjs`
- Frontend client: `node scripts/generate-edge-client.mjs`
- Contract verification: `npm --prefix frontend run api:verify`
- Migration bytes/order: `python3 scripts/validate-migrations.py`

Every backend process exposes liveness, readiness, and version endpoints. The migrator is the only schema writer.
