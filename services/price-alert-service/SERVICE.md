# Price Alert Service

Owns per-user price alerts and trigger history. Supports `above`, `below`, signed `percent_change`, and moving-average `ma_crossing` conditions. Activation fails closed when published market-provider health is stale. Price evaluation reads only `market_data_public.adjusted_daily_bars_v1`.

- `GET/POST /internal/price-alerts`
- `PUT/DELETE /internal/price-alerts/{id}`
- `POST /internal/price-alerts/{id}/dismiss`, `/reactivate`
- `GET /internal/price-alerts/{id}/triggers`
- `POST /internal/worker/run`
