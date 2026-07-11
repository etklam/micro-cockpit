CREATE SCHEMA IF NOT EXISTS market;
CREATE SCHEMA IF NOT EXISTS market_data_public;

CREATE TABLE market.symbols (
  symbol text PRIMARY KEY,
  name text NOT NULL,
  exchange text NOT NULL,
  currency text NOT NULL,
  timezone text NOT NULL,
  active boolean NOT NULL DEFAULT true,
  updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE market.provider_runs (
  id uuid PRIMARY KEY,
  provider text NOT NULL,
  started_at timestamptz NOT NULL,
  completed_at timestamptz,
  status text NOT NULL CHECK (status IN ('running','succeeded','failed')),
  error text,
  rows_received integer NOT NULL DEFAULT 0 CHECK (rows_received >= 0)
);
CREATE TABLE market.daily_bars (
  symbol text NOT NULL REFERENCES market.symbols(symbol),
  trading_date date NOT NULL,
  open numeric(20,8) NOT NULL CHECK (open >= 0),
  high numeric(20,8) NOT NULL CHECK (high >= 0),
  low numeric(20,8) NOT NULL CHECK (low >= 0),
  close numeric(20,8) NOT NULL CHECK (close >= 0),
  volume numeric(28,8) NOT NULL CHECK (volume >= 0),
  provider text NOT NULL,
  provider_run_id uuid NOT NULL REFERENCES market.provider_runs(id),
  ingested_at timestamptz NOT NULL DEFAULT now(),
  published_at timestamptz,
  PRIMARY KEY(symbol,trading_date),
  CHECK (high >= greatest(open,low,close) AND low <= least(open,high,close))
);
CREATE INDEX market_daily_bars_published_idx ON market.daily_bars(symbol,trading_date DESC) WHERE published_at IS NOT NULL;

-- Stable read contracts: consumers must use these, never staging tables.
CREATE VIEW market.published_symbols_v1 AS
SELECT symbol,name,exchange,currency,timezone,active,updated_at FROM market.symbols WHERE active;
CREATE VIEW market.published_daily_bars_v1 AS
SELECT symbol,trading_date,open,high,low,close,volume,provider,published_at
FROM market.daily_bars WHERE published_at IS NOT NULL;
CREATE VIEW market_data_public.adjusted_daily_bars_v1 AS
SELECT symbol,trading_date AS trade_date,close AS adjusted_close,volume
FROM market.daily_bars WHERE published_at IS NOT NULL;
CREATE VIEW market.published_provider_health_v1 AS
SELECT p.provider,
       coalesce(last_success.completed_at, '-infinity'::timestamptz) AS last_success_at,
       (latest.status='succeeded' AND last_success.completed_at >= now() - interval '3 days') AS healthy
FROM (SELECT DISTINCT provider FROM market.provider_runs) p
LEFT JOIN LATERAL (
  SELECT status FROM market.provider_runs r
  WHERE r.provider=p.provider ORDER BY started_at DESC LIMIT 1
) latest ON true
LEFT JOIN LATERAL (
  SELECT completed_at FROM market.provider_runs r
  WHERE r.provider=p.provider AND r.status='succeeded'
  ORDER BY completed_at DESC NULLS LAST LIMIT 1
) last_success ON true;
