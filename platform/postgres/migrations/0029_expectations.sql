-- migration-id: 0029
-- owner: journal-service
-- description: Expectation lifecycle for Observation Updates

ALTER TABLE journal.observation_updates
    ADD CONSTRAINT observation_updates_id_user_unique UNIQUE (id, user_id);

CREATE TABLE journal.expectations (
    id uuid PRIMARY KEY,
    observation_update_id uuid NOT NULL,
    user_id uuid NOT NULL,
    expected_behavior text NOT NULL,
    deadline timestamptz NOT NULL,
    invalidation_condition text NOT NULL,
    confidence text NOT NULL CHECK (confidence IN ('low', 'medium', 'high')),
    market text NOT NULL,
    invalidated_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    deleted_at timestamptz,
    CONSTRAINT expectations_update_owner_fk
        FOREIGN KEY (observation_update_id, user_id)
        REFERENCES journal.observation_updates(id, user_id)
);

CREATE INDEX expectations_update_idx
ON journal.expectations (observation_update_id, user_id)
WHERE deleted_at IS NULL;

CREATE INDEX expectations_readiness_idx
ON journal.expectations (user_id, deadline)
WHERE deleted_at IS NULL;
