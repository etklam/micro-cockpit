# Backend service catalog

| Service | Schema | Public Edge families |
|---|---|---|
| Identity | `identity` | auth, settings, Agent Users, account export/deletion |
| Journal | `journal` | observations, expectations, reviews, decisions, Watchlist, principles, grants, agent sync |
| Market Data | `market`, `market_data_public` | symbols, bars, provider health |
| Tool | `tool` | calculators, presets, saved calculations |

Edge exposes Bootstrap and Calendar composition plus direct proxies. No retired service or compatibility route is registered.

All services are private in Compose and Kubernetes. Runtime roles have no cross-schema writes and no migration-ledger access.
