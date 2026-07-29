# Service catalog

Edge is the only browser-facing backend. Every private process exposes `/health/live`, `/health/ready`, and `/version`.

| Service | Owns | Responsibility |
|---|---|---|
| [Identity](services/identity-service/SERVICE.md) | `identity` | Humans, Agent Users, sessions, API tokens, preferences |
| [Journal](services/journal-service/SERVICE.md) | `journal` | Market Observations, Expectations, reviews, decisions, Trades, Watchlist, grants, incremental sync |
| [Market Data](services/market-data-service/SERVICE.md) | `market`, `market_data_public` | stable Instruments, symbol history, raw and adjusted Daily Close evidence |
| [Tool](services/tool-service/SERVICE.md) | `tool` | calculators, presets, Calculation Snapshots |

Schema ownership is normative in [`contracts/schema-ownership.json`](contracts/schema-ownership.json). Runtime services write only their owned schemas. Cross-service reads use HTTP or an explicitly published view.
