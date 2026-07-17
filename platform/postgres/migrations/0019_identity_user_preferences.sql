-- migration-id: 0019
-- owner: identity-service
-- description: Account appearance preference and settings updated_at

ALTER TABLE identity.users
  ADD COLUMN IF NOT EXISTS appearance text NOT NULL DEFAULT 'system',
  ADD COLUMN IF NOT EXISTS updated_at timestamptz NOT NULL DEFAULT now();

ALTER TABLE identity.users
  DROP CONSTRAINT IF EXISTS users_appearance_check;

ALTER TABLE identity.users
  ADD CONSTRAINT users_appearance_check
  CHECK (appearance IN ('system', 'light', 'dark'));
