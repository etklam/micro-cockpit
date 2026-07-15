-- migration-id: 0006
-- owner: platform-legacy
-- description: Journal deletion event delivery

CREATE TABLE IF NOT EXISTS journal.outbox_events (
  event_id uuid PRIMARY KEY,
  event_type text NOT NULL,
  event_version integer NOT NULL,
  payload jsonb NOT NULL,
  occurred_at timestamptz NOT NULL DEFAULT now(),
  published_at timestamptz
);
CREATE INDEX IF NOT EXISTS journal_outbox_unpublished_idx ON journal.outbox_events(occurred_at) WHERE published_at IS NULL;

ALTER TABLE reminder.inbox_events ADD COLUMN IF NOT EXISTS event_version integer NOT NULL DEFAULT 1;
