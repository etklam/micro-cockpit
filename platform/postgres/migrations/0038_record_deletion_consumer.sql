-- migration-id: 0038
-- owner: reminder-service
-- description: Content-free RecordDeleted inbox and owned-reference tombstones

-- No JSON payload by design: immutable consumer history cannot retain personal content.
CREATE TABLE reminder.record_inbox_events (
    event_id uuid PRIMARY KEY,
    record_id uuid NOT NULL,
    record_type text NOT NULL CHECK (record_type IN (
        'market_observation', 'observation_update', 'expectation',
        'expectation_review', 'action_decision', 'trade'
    )),
    event_version integer NOT NULL CHECK (event_version = 1),
    operation text NOT NULL CHECK (operation = 'deleted'),
    event_time timestamptz NOT NULL,
    processed_at timestamptz
);

CREATE TABLE reminder.record_tombstones (
    record_id uuid NOT NULL,
    record_type text NOT NULL,
    deleted_at timestamptz NOT NULL,
    PRIMARY KEY (record_type,record_id)
);
