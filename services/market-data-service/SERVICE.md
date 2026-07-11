# Market Data Service

Owns symbol metadata, provider ingestion runs, and daily OHLCV bars. Providers push data through the service-key protected admin contract; this service does not call external providers. Consumers use versioned published endpoints/views only.

- `POST /internal/admin/provider-runs`
- `PUT /internal/admin/symbols/{symbol}`
- `PUT /internal/admin/provider-runs/{runId}/bars`
- `POST /internal/admin/provider-runs/{runId}/complete`
- `GET /internal/v1/symbols`, `GET /internal/v1/bars/{symbol}`, `GET /internal/v1/providers/health`
