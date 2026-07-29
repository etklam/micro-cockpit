# Database and domain schema

| Schema | Owner | Core data |
|---|---|---|
| `identity` | Identity | users, credentials, refresh families, API keys, Agent managers, settings |
| `journal` | Journal | Market Observations, updates, Expectations, reviews, decisions, Trades, Watchlist, principles, grants, change logs |
| `market` | Market Data | Instruments, symbol history, provider runs, Daily Close bars |
| `market_data_public` | Market Data | versioned published evidence views |
| `tool` | Tool | presets and saved Calculation Snapshots |
| `platform_migrations` | Migrator | immutable migration ledger |

Important constraints:

- one active API token per Agent User;
- child Journal records preserve owner identity;
- Access Grant dates, subject shapes, and distinct owners are checked;
- stable Instrument IDs are separate from display symbols;
- raw and adjusted Daily Close are both published;
- deleted-record sync contains no personal content;
- runtime roles have DML only on their owned boundary.

Migration `0041_market_observation_cutover.sql` removes the unlaunched legacy tables and schemas. It carries the repository’s single narrow destructive-cutover approval marker.
