-- migration-id: 0036
-- owner: journal-service
-- description: Read-only fixed and ongoing Agent access grants over Market Observation closures

CREATE FUNCTION journal.subject_matches(
    primary_subject jsonb,
    related_subjects jsonb,
    filter_type text,
    filter_name text,
    filter_instrument uuid
) RETURNS boolean
LANGUAGE sql
IMMUTABLE
AS $$
    SELECT CASE
        WHEN filter_instrument IS NOT NULL THEN
            primary_subject->>'instrumentId'=filter_instrument::text
            OR EXISTS (
                SELECT 1 FROM jsonb_array_elements(COALESCE(related_subjects,'[]'::jsonb)) subject
                WHERE subject->>'instrumentId'=filter_instrument::text
            )
        WHEN filter_type IS NOT NULL THEN
            (
                primary_subject->>'type' IN (
                    filter_type,
                    CASE filter_type WHEN 'broad_market' THEN '0' WHEN 'sector' THEN '1' WHEN 'theme' THEN '2' END
                )
                AND lower(primary_subject->>'name')=lower(filter_name)
            )
            OR EXISTS (
                SELECT 1 FROM jsonb_array_elements(COALESCE(related_subjects,'[]'::jsonb)) subject
                WHERE subject->>'type' IN (
                    filter_type,
                    CASE filter_type WHEN 'broad_market' THEN '0' WHEN 'sector' THEN '1' WHEN 'theme' THEN '2' END
                )
                AND lower(subject->>'name')=lower(filter_name)
            )
        ELSE false
    END
$$;

CREATE TABLE journal.agent_access_grants (
    id uuid PRIMARY KEY,
    owner_user_id uuid NOT NULL,
    agent_user_id uuid NOT NULL,
    mode text NOT NULL CHECK (mode IN ('fixed', 'ongoing')),
    from_date date NOT NULL,
    to_date date NOT NULL,
    subject_type text CHECK (subject_type IN ('broad_market', 'sector', 'theme')),
    subject_name text,
    instrument_id uuid,
    expires_at timestamptz,
    revoked_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT agent_access_grants_date_check CHECK (from_date <= to_date),
    CONSTRAINT agent_access_grants_subject_check CHECK (
        (subject_type IS NULL AND subject_name IS NULL)
        OR (subject_type IS NOT NULL AND length(btrim(subject_name)) BETWEEN 1 AND 120)
    ),
    CONSTRAINT agent_access_grants_distinct_users CHECK (owner_user_id <> agent_user_id)
);

CREATE INDEX agent_access_grants_agent_active_idx
ON journal.agent_access_grants (agent_user_id, created_at)
WHERE revoked_at IS NULL;

CREATE TABLE journal.agent_access_grant_records (
    grant_id uuid NOT NULL REFERENCES journal.agent_access_grants(id) ON DELETE CASCADE,
    record_type text NOT NULL CHECK (record_type IN (
        'market_observation', 'observation_update', 'expectation',
        'expectation_review', 'action_decision', 'trade'
    )),
    record_id uuid NOT NULL,
    PRIMARY KEY (grant_id, record_type, record_id)
);
