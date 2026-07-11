# Published database views

Published views are narrow, versioned, read-only contracts. Their schema name ends in `_public`; consumers receive `USAGE` and `SELECT` on named views only. They never receive privileges on the producer's private schema.

Current contract: `market_data_public.adjusted_daily_bars_v1(symbol, trade_date, adjusted_close, volume)`. Its columns may not be removed, renamed, retyped, or semantically changed in place. Additive nullable columns are allowed. Breaking changes require `_v2`; deploy producer, migrate consumers, observe, then retire `_v1` in a later release. Consumers must tolerate zero rows and stale/unavailable provider state explicitly.
