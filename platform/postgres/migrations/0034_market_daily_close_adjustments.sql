-- migration-id: 0034
-- owner: market-data-service
-- description: Raw and corporate-action-adjusted Daily Close publication

ALTER TABLE market.daily_bars
    ADD COLUMN adjusted_close numeric(20,8);

UPDATE market.daily_bars
SET adjusted_close = close;

ALTER TABLE market.daily_bars
    ALTER COLUMN adjusted_close SET NOT NULL,
    ADD CONSTRAINT market_daily_bars_adjusted_close_nonnegative CHECK (adjusted_close >= 0);

DROP VIEW market_data_public.adjusted_daily_bars_v1;
DROP VIEW market.published_daily_bars_v1;

ALTER TABLE market.daily_bars
    DROP CONSTRAINT daily_bars_pkey,
    ADD PRIMARY KEY (symbol, trading_date, provider_run_id);

CREATE VIEW market.published_daily_bars_v1 AS
SELECT DISTINCT ON (symbol,trading_date)
       symbol,trading_date,open,high,low,close AS raw_close,adjusted_close,
       volume,provider,published_at
FROM market.daily_bars
WHERE published_at IS NOT NULL
ORDER BY symbol,trading_date,published_at DESC,provider_run_id DESC;

CREATE VIEW market_data_public.adjusted_daily_bars_v1 AS
SELECT symbol,trading_date AS trade_date,adjusted_close,volume
FROM market.published_daily_bars_v1;
