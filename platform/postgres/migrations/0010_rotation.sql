-- migration-id: 0010
-- owner: platform-legacy
-- description: Market rotation schema

CREATE SCHEMA IF NOT EXISTS rotation;

CREATE TABLE IF NOT EXISTS rotation.market_rotation_universes (
  id uuid PRIMARY KEY,
  code text NOT NULL UNIQUE CHECK (code ~ '^[A-Z0-9_-]+$'),
  name text NOT NULL CHECK (length(trim(name)) > 0),
  rank_scope text NOT NULL DEFAULT 'universe' CHECK (rank_scope IN ('universe','sector')),
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS rotation.market_rotation_universe_symbols (
  universe_id uuid NOT NULL REFERENCES rotation.market_rotation_universes(id) ON DELETE CASCADE,
  symbol text NOT NULL CHECK (symbol ~ '^[A-Z0-9.-]+$'),
  label text NOT NULL,
  sector text,
  sort_order integer NOT NULL DEFAULT 0,
  PRIMARY KEY (universe_id, symbol)
);
CREATE TABLE IF NOT EXISTS rotation.batch_runs (
  id uuid PRIMARY KEY,
  universe_id uuid NOT NULL REFERENCES rotation.market_rotation_universes(id) ON DELETE CASCADE,
  snapshot_date date NOT NULL,
  formula_version text NOT NULL,
  status text NOT NULL CHECK (status IN ('running','completed','insufficient_data','failed')),
  source_max_date date,
  started_at timestamptz NOT NULL DEFAULT now(),
  finished_at timestamptz,
  error text,
  UNIQUE (universe_id, snapshot_date, formula_version)
);
CREATE TABLE IF NOT EXISTS rotation.market_rotation_snapshots (
  universe_id uuid NOT NULL REFERENCES rotation.market_rotation_universes(id) ON DELETE CASCADE,
  snapshot_date date NOT NULL,
  symbol text NOT NULL,
  rank_scope text NOT NULL,
  sector text,
  close numeric,
  return_2w numeric,
  return_1m numeric,
  return_3m numeric,
  above_ma20 boolean,
  above_ma50 boolean,
  above_ma200 boolean,
  rank_2w integer,
  percentile_2w numeric,
  status text NOT NULL CHECK (status IN ('ok','insufficient_data')),
  formula_version text NOT NULL,
  calculated_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (universe_id, snapshot_date, symbol)
);
CREATE INDEX IF NOT EXISTS rotation_snapshots_lookup_idx ON rotation.market_rotation_snapshots(universe_id,snapshot_date,rank_scope,rank_2w);
CREATE TABLE IF NOT EXISTS rotation.sector_breadth_snapshots (
  universe_id uuid NOT NULL REFERENCES rotation.market_rotation_universes(id) ON DELETE CASCADE,
  snapshot_date date NOT NULL,
  sector text NOT NULL,
  member_count integer NOT NULL,
  available_count integer NOT NULL,
  above_ma20_percent numeric,
  above_ma50_percent numeric,
  above_ma200_percent numeric,
  status text NOT NULL CHECK (status IN ('ok','insufficient_data')),
  formula_version text NOT NULL,
  calculated_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (universe_id, snapshot_date, sector)
);
CREATE TABLE IF NOT EXISTS rotation.market_state_snapshots (
  universe_id uuid NOT NULL REFERENCES rotation.market_rotation_universes(id) ON DELETE CASCADE,
  snapshot_date date NOT NULL,
  state text,
  breadth_percent numeric,
  benchmark_symbol text,
  benchmark_above_ma200 boolean,
  status text NOT NULL CHECK (status IN ('ok','insufficient_data')),
  formula_version text NOT NULL,
  calculated_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (universe_id, snapshot_date)
);
