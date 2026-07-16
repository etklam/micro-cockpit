-- migration-id: 0016
-- owner: market-data-service
-- description: Published daily bar open and close price contract

CREATE VIEW market_data_public.daily_bar_prices_v1 AS
SELECT symbol,
       trading_date AS trade_date,
       open AS open_price,
       close AS close_price,
       published_at
FROM market.daily_bars
WHERE published_at IS NOT NULL;
