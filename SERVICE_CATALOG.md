# Service catalog

The Edge API is the only browser-facing backend. Domain services expose `/internal/*` contracts to the Edge API or workers; every process exposes `/health/live`, `/health/ready`, and `/version`.

| Service | Owns | Published reads / events |
|---|---|---|
| [identity-service](services/identity-service/SERVICE.md) | `identity` | JWT/OIDC metadata and JWKS |
| [journal-service](services/journal-service/SERVICE.md) | `journal` | `DiaryDeleted.v1` |
| [performance-service](services/performance-service/SERVICE.md) | `performance` | API only |
| [discipline-service](services/discipline-service/SERVICE.md) | `discipline` | API only |
| [reminder-service](services/reminder-service/SERVICE.md) | `reminder` | consumes `DiaryDeleted.v1` |
| [market-data-service](services/market-data-service/SERVICE.md) | `market`, `market_data_public` | versioned daily-bar views |
| [price-alert-service](services/price-alert-service/SERVICE.md) | `price_alert` | reads the market-data published view |
| [rotation-service](services/rotation-service/SERVICE.md) | `rotation` | reads the market-data published view |
| [stock-research-service](services/stock-research-service/SERVICE.md) | `stock_research` | API only |
| [partner-service](services/partner-service/SERVICE.md) | `partner` | API only; does not copy shared records |
| [content-service](services/content-service/SERVICE.md) | `content` | public/admin API |
| [operations-service](services/operations-service/SERVICE.md) | `operations` | audit/job/health API |
| [tool-service](services/tool-service/SERVICE.md) | `tool` | calculators, presets, saved snapshots; validates optional sources through Journal |

Schema ownership is normative in [`contracts/schema-ownership.json`](contracts/schema-ownership.json). A service may write only its owned schemas. Cross-service reads use an HTTP contract or a versioned published view, never another service's private tables.
