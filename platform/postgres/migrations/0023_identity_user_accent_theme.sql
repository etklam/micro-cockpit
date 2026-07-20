-- migration-id: 0023
-- owner: identity-service
-- description: Account accent theme preference (green | red)

ALTER TABLE identity.users
  ADD COLUMN IF NOT EXISTS accent_theme text NOT NULL DEFAULT 'green';

ALTER TABLE identity.users
  DROP CONSTRAINT IF EXISTS users_accent_theme_check;

ALTER TABLE identity.users
  ADD CONSTRAINT users_accent_theme_check
  CHECK (accent_theme IN ('green', 'red'));
