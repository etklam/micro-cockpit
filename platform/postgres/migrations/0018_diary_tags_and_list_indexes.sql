-- migration-id: 0018
-- owner: journal-service
-- description: First-class diary tags and list query indexes

CREATE TABLE IF NOT EXISTS journal.diary_tags (
  diary_id uuid NOT NULL,
  user_id uuid NOT NULL,
  tag text NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (diary_id, tag),
  CONSTRAINT diary_tags_tag_nonempty CHECK (length(tag) > 0 AND length(tag) <= 32),
  CONSTRAINT diary_tags_diary_user_fk
    FOREIGN KEY (diary_id, user_id) REFERENCES journal.diaries(id, user_id)
);

-- Exact tag filter: user + tag → diary ids
CREATE INDEX IF NOT EXISTS diary_tags_user_tag_diary_idx
  ON journal.diary_tags (user_id, tag, diary_id);

-- Active diary list order: local_date DESC, created_at DESC, id DESC
CREATE INDEX IF NOT EXISTS diaries_user_active_order_idx
  ON journal.diaries (user_id, local_date DESC, created_at DESC, id DESC)
  WHERE deleted_at IS NULL;

-- Symbol filter via EXISTS without row multiplication
CREATE INDEX IF NOT EXISTS transactions_user_symbol_diary_idx
  ON journal.transactions (user_id, symbol, diary_id)
  WHERE deleted_at IS NULL;
