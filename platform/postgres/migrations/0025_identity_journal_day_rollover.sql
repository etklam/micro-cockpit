-- migration-id: 0025
-- owner: identity-service
-- description: Configurable local rollover time for Journal Days

ALTER TABLE identity.users
ADD COLUMN journal_day_rollover time NOT NULL DEFAULT '00:00';
