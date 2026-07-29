-- migration-id: 0040
-- owner: reminder-service
-- description: Retire and content-scrub legacy reminder event tables

UPDATE reminder.inbox_events SET payload = '{}'::jsonb;
ALTER TABLE reminder.inbox_events
  ADD CONSTRAINT retired_diary_deleted_inbox_content_free CHECK (payload = '{}'::jsonb);
ALTER TABLE reminder.inbox_events RENAME TO retired_diary_deleted_inbox;

UPDATE reminder.outbox_events SET payload = '{}'::jsonb;
ALTER TABLE reminder.outbox_events
  ADD CONSTRAINT retired_diary_deleted_outbox_content_free CHECK (payload = '{}'::jsonb);
ALTER TABLE reminder.outbox_events RENAME TO retired_diary_deleted_outbox;
