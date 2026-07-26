-- migration-id: 0028
-- owner: journal-service
-- description: Structured Observation Update enrichment

ALTER TABLE journal.observation_updates
    ADD COLUMN signal text,
    ADD COLUMN interpretation text,
    ADD COLUMN mental_state text,
    ADD COLUMN tags text[] NOT NULL DEFAULT '{}',
    ADD COLUMN primary_subject jsonb,
    ADD COLUMN related_subjects jsonb NOT NULL DEFAULT '[]'::jsonb,
    ADD COLUMN evidence jsonb,
    ADD CONSTRAINT observation_updates_related_subjects_array CHECK (jsonb_typeof(related_subjects) = 'array'),
    ADD CONSTRAINT observation_updates_primary_subject_object CHECK (primary_subject IS NULL OR jsonb_typeof(primary_subject) = 'object'),
    ADD CONSTRAINT observation_updates_evidence_object CHECK (evidence IS NULL OR jsonb_typeof(evidence) = 'object');

CREATE INDEX observation_updates_tags_gin_idx
ON journal.observation_updates USING gin (tags)
WHERE deleted_at IS NULL;

CREATE INDEX observation_updates_subjects_gin_idx
ON journal.observation_updates USING gin (primary_subject, related_subjects)
WHERE deleted_at IS NULL;
