-- migration-id: 0014
-- owner: platform-legacy
-- description: Structured diary reviews

CREATE TABLE IF NOT EXISTS journal.diary_reviews (
  diary_id uuid PRIMARY KEY REFERENCES journal.diaries(id),
  user_id uuid NOT NULL,
  thesis text,
  planned_action text,
  actual_action text,
  emotion text CHECK (emotion IS NULL OR emotion IN ('calm','confident','uncertain','anxious','fomo','frustrated','overconfident','other')),
  discipline_score smallint CHECK (discipline_score IS NULL OR discipline_score BETWEEN 1 AND 5),
  execution_score smallint CHECK (execution_score IS NULL OR execution_score BETWEEN 1 AND 5),
  process_assessment text CHECK (process_assessment IS NULL OR process_assessment IN ('good','mixed','poor')),
  mistake_tags text[] NOT NULL DEFAULT '{}',
  lesson text,
  next_action text,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS diary_reviews_user_idx ON journal.diary_reviews (user_id);
