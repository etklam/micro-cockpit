-- migration-id: 0009
-- owner: platform-legacy
-- description: Price alert schema

CREATE SCHEMA IF NOT EXISTS price_alert;
CREATE TABLE price_alert.alerts (
  id uuid PRIMARY KEY,
  user_id uuid NOT NULL,
  symbol text NOT NULL,
  condition_type text NOT NULL CHECK (condition_type IN ('above','below','percent_change','ma_crossing')),
  threshold numeric(20,8) NOT NULL,
  lookback_days integer,
  direction text CHECK (direction IN ('above','below')),
  status text NOT NULL CHECK (status IN ('active','triggered','dismissed')),
  baseline_close numeric(20,8),
  last_evaluated_date date,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  CHECK ((condition_type='percent_change' AND baseline_close IS NOT NULL AND baseline_close > 0) OR condition_type<>'percent_change'),
  CHECK ((condition_type='ma_crossing' AND lookback_days BETWEEN 2 AND 250 AND direction IS NOT NULL) OR condition_type<>'ma_crossing')
);
CREATE INDEX price_alert_active_idx ON price_alert.alerts(symbol) WHERE status='active';
CREATE TABLE price_alert.triggers (
  id uuid PRIMARY KEY,
  alert_id uuid NOT NULL REFERENCES price_alert.alerts(id) ON DELETE CASCADE,
  trading_date date NOT NULL,
  observed_close numeric(20,8) NOT NULL,
  triggered_at timestamptz NOT NULL DEFAULT now(),
  dismissed_at timestamptz,
  UNIQUE(alert_id,trading_date)
);
