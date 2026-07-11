CREATE SCHEMA IF NOT EXISTS journal;
CREATE SCHEMA IF NOT EXISTS performance;

CREATE TABLE IF NOT EXISTS journal.diaries (
  id uuid PRIMARY KEY,
  user_id uuid NOT NULL,
  local_date date NOT NULL,
  title text NOT NULL CHECK (length(trim(title)) > 0),
  content text NOT NULL DEFAULT '',
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  deleted_at timestamptz
);
CREATE INDEX IF NOT EXISTS diaries_user_date_idx ON journal.diaries (user_id, local_date DESC) WHERE deleted_at IS NULL;

CREATE TABLE IF NOT EXISTS journal.transactions (
  id uuid PRIMARY KEY,
  diary_id uuid NOT NULL REFERENCES journal.diaries(id),
  user_id uuid NOT NULL,
  symbol text NOT NULL,
  side text NOT NULL CHECK (side IN ('buy', 'sell')),
  quantity numeric(20,8) NOT NULL CHECK (quantity > 0),
  price numeric(20,8) NOT NULL CHECK (price > 0),
  traded_at timestamptz NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS performance.daily_performances (
  user_id uuid NOT NULL,
  local_date date NOT NULL,
  pnl_amount numeric(20,4) NOT NULL,
  capital_base numeric(20,4) CHECK (capital_base > 0),
  note text NOT NULL DEFAULT '',
  updated_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (user_id, local_date)
);
