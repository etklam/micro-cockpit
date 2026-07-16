# Price Alert Service

Owns per-user price alerts and trigger history. Supports daily-bar `above` and `below` alerts evaluated against the published open or close price. Close is the default. Existing signed `percent_change` and moving-average `ma_crossing` conditions remain close-only for compatibility. Activation fails closed when published market-provider health is stale.

Price evaluation reads only `market_data_public.daily_bar_prices_v1`. The worker evaluates an alert only when that view exposes a newer trade date than `last_evaluated_date`; its default and minimum schedule interval is one hour. Open-price alerts are evaluated after the complete daily bar is published and are not real-time market-open notifications.

- `GET/POST /internal/price-alerts`
- `PUT/DELETE /internal/price-alerts/{id}`
- `POST /internal/price-alerts/{id}/dismiss`, `/reactivate`
- `GET /internal/price-alerts/{id}/triggers`
- `POST /internal/worker/run`
