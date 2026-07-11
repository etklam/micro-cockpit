CREATE SCHEMA IF NOT EXISTS stock_research;

CREATE TABLE IF NOT EXISTS stock_research.stocks (
  id uuid PRIMARY KEY,
  symbol text NOT NULL UNIQUE CHECK (symbol = upper(symbol) AND length(trim(symbol)) > 0),
  name text NOT NULL CHECK (length(trim(name)) > 0),
  exchange text NOT NULL DEFAULT '',
  asset_type text NOT NULL DEFAULT 'stock' CHECK (asset_type = 'stock'),
  created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS stock_research.watchlist_items (
  user_id uuid NOT NULL,
  stock_id uuid NOT NULL REFERENCES stock_research.stocks(id),
  created_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (user_id, stock_id)
);

CREATE TABLE IF NOT EXISTS stock_research.stock_notes (
  user_id uuid NOT NULL,
  stock_id uuid NOT NULL REFERENCES stock_research.stocks(id),
  content text NOT NULL DEFAULT '',
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (user_id, stock_id)
);

CREATE TABLE IF NOT EXISTS stock_research.stock_timeline_records (
  id uuid PRIMARY KEY,
  user_id uuid NOT NULL,
  stock_id uuid NOT NULL REFERENCES stock_research.stocks(id),
  event_time timestamptz NOT NULL,
  source_type text NOT NULL CHECK (length(trim(source_type)) > 0),
  title text NOT NULL CHECK (length(trim(title)) > 0),
  content text NOT NULL CHECK (length(trim(content)) > 0),
  diary_id uuid,
  correction_of_id uuid REFERENCES stock_research.stock_timeline_records(id),
  created_at timestamptz NOT NULL DEFAULT now(),
  CHECK (correction_of_id IS NULL OR correction_of_id <> id)
);
CREATE INDEX IF NOT EXISTS stock_timeline_user_stock_event_idx
  ON stock_research.stock_timeline_records(user_id, stock_id, event_time DESC, id);

CREATE OR REPLACE FUNCTION stock_research.reject_timeline_mutation() RETURNS trigger
LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'stock timeline records are immutable'; END $$;
DROP TRIGGER IF EXISTS stock_timeline_immutable ON stock_research.stock_timeline_records;
CREATE TRIGGER stock_timeline_immutable BEFORE UPDATE OR DELETE ON stock_research.stock_timeline_records
FOR EACH ROW EXECUTE FUNCTION stock_research.reject_timeline_mutation();
