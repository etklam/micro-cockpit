# Service catalog

The Edge API is the only browser-facing backend. Domain services expose `/internal/*` contracts to the Edge API or workers; every process exposes `/health/live`, `/health/ready`, and `/version`.

| Service | Owns | Published reads / events |
|---|---|---|
| identity-service | `identity` | JWT/OIDC metadata and JWKS |
| journal-service | `journal` | `DiaryDeleted.v1` |
| performance-service | `performance` | API only |
| discipline-service | `discipline` | API only |
| reminder-service | `reminder` | consumes `DiaryDeleted.v1` |
| market-data-service | `market`, `market_data_public` | `market_data_public.adjusted_daily_bars_v1` |
| price-alert-service | `price_alert` | reads the market-data published view |
| rotation-service | `rotation` | reads the market-data published view |
| stock-research-service | `stock_research` | API only |
| partner-service | `partner` | API only; does not copy shared records |
| content-service | `content` | public/admin API |
| operations-service | `operations` | audit/job/health API |
| tool-service | none (stateless) | calculator API |

Schema ownership is normative in [`contracts/schema-ownership.json`](contracts/schema-ownership.json). A service may write only its owned schemas. Cross-service reads use an HTTP contract or a versioned published view, never another service's private tables.
