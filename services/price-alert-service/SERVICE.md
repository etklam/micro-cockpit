# Price Alert Service

Owns per-user price alerts and trigger history. Supports daily-bar `above` and `below` alerts evaluated against the published open or close price. Close is the default. Existing signed `percent_change` and moving-average `ma_crossing` conditions remain close-only for compatibility. Activation fails closed when published market-provider health is stale.

Price evaluation reads only `market_data_public.daily_bar_prices_v1`. The worker claim joins each active alert to its latest published trade date, so only alerts newer than `last_evaluated_date` consume the deterministic, locked batch. Concurrent workers lock alert rows with `SKIP LOCKED`; its default and minimum schedule interval is one hour. Open-price alerts are evaluated after the complete daily bar is published and are not real-time market-open notifications.

Dismiss changes active or triggered alerts to `dismissed`. Dismissing a triggered alert also timestamps its latest undismissed trigger in the same transaction; dismissing an active alert only pauses it. Reactivation preserves `last_evaluated_date`, so the alert waits for a newer published bar.

- `GET/POST /internal/price-alerts`
- `PUT/DELETE /internal/price-alerts/{id}`
- `POST /internal/price-alerts/{id}/dismiss`, `/reactivate`
- `GET /internal/price-alerts/{id}/triggers`
- `POST /internal/worker/run`
