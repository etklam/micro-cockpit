-- migration-id: 0002
-- owner: platform-legacy
-- description: Initial identity schema

CREATE SCHEMA IF NOT EXISTS identity;

CREATE TABLE IF NOT EXISTS identity.users (
  id uuid PRIMARY KEY,
  email text NOT NULL,
  display_name text NOT NULL,
  timezone text NOT NULL DEFAULT 'Asia/Taipei',
  base_currency char(3) NOT NULL DEFAULT 'USD',
  role text NOT NULL DEFAULT 'user',
  account_type text NOT NULL DEFAULT 'human',
  status text NOT NULL DEFAULT 'active',
  status_version integer NOT NULL DEFAULT 1,
  created_at timestamptz NOT NULL DEFAULT now(),
  UNIQUE (email)
);

CREATE TABLE IF NOT EXISTS identity.user_credentials (
  user_id uuid PRIMARY KEY REFERENCES identity.users(id),
  password_salt bytea NOT NULL,
  password_hash bytea NOT NULL,
  iterations integer NOT NULL
);

CREATE TABLE IF NOT EXISTS identity.refresh_tokens (
  id uuid PRIMARY KEY,
  user_id uuid NOT NULL REFERENCES identity.users(id),
  family_id uuid NOT NULL,
  token_hash bytea NOT NULL UNIQUE,
  expires_at timestamptz NOT NULL,
  used_at timestamptz,
  revoked_at timestamptz,
  created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS refresh_tokens_family_idx ON identity.refresh_tokens(family_id);
