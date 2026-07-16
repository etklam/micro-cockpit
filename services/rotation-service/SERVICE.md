# Rotation Service

Owns ETF universes and versioned rotation, breadth, and market-state snapshots. It reads only the published `market_data_public.adjusted_daily_bars_v1(symbol, trade_date, adjusted_close, volume)` contract.

## Endpoints

- `GET/POST /internal/rotation/universes`
- `PUT/DELETE /internal/rotation/universes/{id}`
- `PUT /internal/rotation/universes/{id}/symbols`
- `POST /internal/rotation/universes/{id}/calculate?date=YYYY-MM-DD`
- `GET /internal/rotation/monitor?universe=CODE&date=YYYY-MM-DD`
- `GET /health/live`, `GET /health/ready`, `GET /version`

`rotation-v1` uses adjusted-close returns at 10, 20, and 63 trading-session lags and 20/50/200-session simple moving averages. Rank and percent-rank are partitioned by universe, snapshot date, and configured scope. Missing lookback data remains `null` with `insufficient_data`.

The monitor exposes the stored partition semantics through `universe.rankScope` and `etfs[].rankGroup`. Universe scope uses the universe code as every row's rank group. Sector scope uses the sector name, or `Unclassified` when the sector is absent. The raw `percentile2w` value is in the inclusive `0` to `1` range; percentage conversion is presentation only.
