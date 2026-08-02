-- migration-id: 0043
-- owner: identity-service
-- description: Add Simplified Chinese account UI locale

ALTER TABLE identity.users
  DROP CONSTRAINT IF EXISTS users_locale_check;

ALTER TABLE identity.users
  ADD CONSTRAINT users_locale_check
  CHECK (locale IN ('en', 'zh-Hant', 'zh-Hans'));
