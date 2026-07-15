-- migration-id: 0005
-- owner: platform-legacy
-- description: Initial reminder schema

CREATE SCHEMA IF NOT EXISTS reminder;
CREATE TABLE IF NOT EXISTS reminder.diary_alerts (
  id uuid PRIMARY KEY,
  user_id uuid NOT NULL,
  diary_id uuid NOT NULL,
  start_local_date date NOT NULL,
  next_local_date date,
  local_time time NOT NULL,
  timezone text NOT NULL,
  repeat_mode text NOT NULL CHECK (repeat_mode IN ('none','week','month')),
  recurrence_end_local_date date NOT NULL,
  next_trigger_at timestamptz,
  status text NOT NULL CHECK (status IN ('active','dismissed','expired')),
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS diary_alerts_due_idx ON reminder.diary_alerts(next_trigger_at) WHERE status='active';

CREATE TABLE IF NOT EXISTS reminder.reminder_delivery_attempts (
  id uuid PRIMARY KEY,
  diary_alert_id uuid NOT NULL REFERENCES reminder.diary_alerts(id),
  scheduled_for timestamptz NOT NULL,
  claimed_at timestamptz NOT NULL DEFAULT now(),
  delivered_at timestamptz,
  status text NOT NULL CHECK (status IN ('claimed','delivered','failed')),
  error text,
  UNIQUE(diary_alert_id, scheduled_for)
);

CREATE TABLE IF NOT EXISTS reminder.inbox_events (
  event_id uuid PRIMARY KEY,
  event_type text NOT NULL,
  payload jsonb NOT NULL,
  received_at timestamptz NOT NULL DEFAULT now(),
  processed_at timestamptz
);
CREATE TABLE IF NOT EXISTS reminder.outbox_events (
  event_id uuid PRIMARY KEY,
  event_type text NOT NULL,
  payload jsonb NOT NULL,
  occurred_at timestamptz NOT NULL DEFAULT now(),
  published_at timestamptz
);
