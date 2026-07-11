CREATE TABLE IF NOT EXISTS identity.api_keys (
  id uuid PRIMARY KEY,
  user_id uuid NOT NULL REFERENCES identity.users(id),
  created_by uuid NOT NULL REFERENCES identity.users(id),
  name text NOT NULL,
  key_hash bytea NOT NULL UNIQUE,
  scopes text[] NOT NULL DEFAULT '{}',
  expires_at timestamptz,
  revoked_at timestamptz,
  created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS api_keys_user_idx ON identity.api_keys(user_id) WHERE revoked_at IS NULL;
