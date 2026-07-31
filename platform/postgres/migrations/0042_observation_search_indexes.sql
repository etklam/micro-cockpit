-- migration-id: 0042
-- owner: journal-service
-- description: Add trigram and normalized subject search indexes

CREATE EXTENSION IF NOT EXISTS pg_trgm;

CREATE INDEX observation_updates_content_trgm_gin_idx
ON journal.observation_updates USING gin (content gin_trgm_ops)
WHERE deleted_at IS NULL;

CREATE FUNCTION journal.observation_subject_search_key(
    primary_subject jsonb,
    related_subjects jsonb
) RETURNS jsonb
LANGUAGE sql
IMMUTABLE
PARALLEL SAFE
AS $$
    WITH subjects AS (
        SELECT primary_subject AS subject
        WHERE primary_subject IS NOT NULL
        UNION ALL
        SELECT value
        FROM jsonb_array_elements(COALESCE(related_subjects, '[]'::jsonb))
    )
    SELECT COALESCE(
        jsonb_agg(
            jsonb_strip_nulls(
                jsonb_build_object(
                    'type', CASE subject->>'type'
                        WHEN '0' THEN 'broad_market'
                        WHEN '1' THEN 'sector'
                        WHEN '2' THEN 'theme'
                        WHEN '3' THEN 'instrument'
                        ELSE lower(subject->>'type')
                    END,
                    'name', lower(subject->>'name'),
                    'instrumentId', subject->>'instrumentId',
                    'market', upper(subject->>'market'),
                    'symbol', upper(subject->>'symbol')
                )
            )
        ),
        '[]'::jsonb
    )
    FROM subjects
    WHERE jsonb_typeof(subject) = 'object';
$$;

CREATE INDEX observation_updates_subject_search_gin_idx
ON journal.observation_updates USING gin (
    journal.observation_subject_search_key(primary_subject, related_subjects)
)
WHERE deleted_at IS NULL;
