-- migration-id: 0039
-- owner: journal-service
-- description: Retire and content-scrub legacy DiaryDeleted outbox

UPDATE journal.outbox_events SET payload = '{}'::jsonb;
ALTER TABLE journal.outbox_events
  ADD CONSTRAINT retired_diary_deleted_outbox_content_free CHECK (payload = '{}'::jsonb);
ALTER TABLE journal.outbox_events RENAME TO retired_diary_deleted_outbox;
