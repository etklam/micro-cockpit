-- migration-id: 0024
-- owner: tool-service
-- description: User-scoped tool presets and validated calculation history

CREATE SCHEMA IF NOT EXISTS tool;

CREATE TABLE tool.presets (
  id uuid PRIMARY KEY,
  user_id uuid NOT NULL,
  name text NOT NULL CHECK (char_length(name) BETWEEN 1 AND 80),
  tool_type text NOT NULL CHECK (tool_type IN ('position-sizing','risk-reward','average-cost','profit-loss')),
  schema_version integer NOT NULL DEFAULT 1 CHECK (schema_version = 1),
  inputs jsonb NOT NULL,
  currency char(3),
  last_used_at timestamptz,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE tool.saved_calculations (
  id uuid PRIMARY KEY,
  user_id uuid NOT NULL,
  tool_type text NOT NULL CHECK (tool_type IN ('position-sizing','risk-reward','average-cost','profit-loss')),
  schema_version integer NOT NULL DEFAULT 1 CHECK (schema_version = 1),
  inputs jsonb NOT NULL,
  output jsonb NOT NULL,
  currency char(3) NOT NULL,
  symbol text,
  source_diary_id uuid,
  source_transaction_id uuid,
  idempotency_key text NOT NULL CHECK (char_length(idempotency_key) BETWEEN 8 AND 100),
  note text,
  created_at timestamptz NOT NULL DEFAULT now(),
  CHECK (symbol IS NULL OR char_length(symbol) BETWEEN 1 AND 20),
  CHECK (note IS NULL OR char_length(note) <= 1000),
  CHECK ((source_transaction_id IS NULL) OR (source_diary_id IS NOT NULL)),
  UNIQUE (user_id, idempotency_key)
);

CREATE INDEX tool_saved_calculations_user_recent_idx ON tool.saved_calculations(user_id, created_at DESC, id DESC);
CREATE INDEX tool_presets_user_tool_idx ON tool.presets(user_id, tool_type, updated_at DESC);
CREATE UNIQUE INDEX tool_presets_user_name_idx ON tool.presets(user_id, lower(name));
