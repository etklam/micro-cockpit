-- migration-id: 0003
-- owner: platform-legacy
-- description: Extend journal transactions

ALTER TABLE journal.transactions
  ADD COLUMN IF NOT EXISTS currency char(3) NOT NULL DEFAULT 'USD',
  ADD COLUMN IF NOT EXISTS notes text NOT NULL DEFAULT '',
  ADD COLUMN IF NOT EXISTS updated_at timestamptz NOT NULL DEFAULT now(),
  ADD COLUMN IF NOT EXISTS deleted_at timestamptz;
CREATE INDEX IF NOT EXISTS transactions_diary_idx ON journal.transactions(user_id, diary_id) WHERE deleted_at IS NULL;
