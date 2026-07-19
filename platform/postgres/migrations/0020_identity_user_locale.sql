-- migration-id: 0020
-- owner: identity-service
-- description: Account UI locale preference (en | zh-Hant)

ALTER TABLE identity.users
  ADD COLUMN IF NOT EXISTS locale text NOT NULL DEFAULT 'en';

ALTER TABLE identity.users
  DROP CONSTRAINT IF EXISTS users_locale_check;

ALTER TABLE identity.users
  ADD CONSTRAINT users_locale_check
  CHECK (locale IN ('en', 'zh-Hant'));
