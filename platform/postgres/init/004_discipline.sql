CREATE SCHEMA IF NOT EXISTS discipline;
CREATE TABLE IF NOT EXISTS discipline.disciplines (
  id uuid PRIMARY KEY,
  user_id uuid NOT NULL,
  content text NOT NULL CHECK (length(trim(content)) > 0),
  position integer NOT NULL CHECK (position >= 0),
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT disciplines_user_position_key UNIQUE(user_id, position) DEFERRABLE INITIALLY DEFERRED
);
