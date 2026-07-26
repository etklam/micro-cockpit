-- migration-id: 0026
-- owner: journal-service
-- description: Daily Market Observations with timestamped mutable updates

CREATE TABLE journal.market_observations (
    id uuid PRIMARY KEY,
    user_id uuid NOT NULL,
    journal_day date NOT NULL,
    timezone text NOT NULL,
    rollover_time time NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    deleted_at timestamptz,
    UNIQUE (id, user_id)
);

CREATE UNIQUE INDEX market_observations_active_day_uidx
ON journal.market_observations (user_id, journal_day)
WHERE deleted_at IS NULL;

CREATE TABLE journal.observation_updates (
    id uuid PRIMARY KEY,
    market_observation_id uuid NOT NULL,
    user_id uuid NOT NULL,
    content text NOT NULL CHECK (length(btrim(content)) > 0),
    sequence bigint GENERATED ALWAYS AS IDENTITY,
    recorded_at timestamptz NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    deleted_at timestamptz,
    CONSTRAINT observation_updates_owner_fk
        FOREIGN KEY (market_observation_id, user_id)
        REFERENCES journal.market_observations (id, user_id)
);

CREATE INDEX observation_updates_timeline_idx
ON journal.observation_updates (market_observation_id, sequence)
WHERE deleted_at IS NULL;
