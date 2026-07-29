-- migration-id: 0031
-- owner: journal-service
-- description: Action decisions and optional trade evidence

CREATE TABLE journal.action_decisions (
    id uuid PRIMARY KEY,
    observation_update_id uuid NOT NULL,
    expectation_id uuid,
    user_id uuid NOT NULL,
    intent text NOT NULL CHECK (intent IN ('trade', 'continue_observing', 'avoid_trade')),
    reason text NOT NULL CHECK (length(btrim(reason)) BETWEEN 1 AND 2000),
    recorded_at timestamptz NOT NULL DEFAULT now(),
    execution_review text CHECK (execution_review IN ('followed', 'partially_followed', 'deviated')),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    deleted_at timestamptz,
    CONSTRAINT action_decisions_id_user_unique UNIQUE (id, user_id),
    CONSTRAINT action_decisions_update_owner_fk
        FOREIGN KEY (observation_update_id, user_id)
        REFERENCES journal.observation_updates(id, user_id),
    CONSTRAINT action_decisions_expectation_owner_fk
        FOREIGN KEY (expectation_id, user_id)
        REFERENCES journal.expectations(id, user_id)
);

CREATE INDEX action_decisions_update_idx
ON journal.action_decisions (observation_update_id, recorded_at)
WHERE deleted_at IS NULL;

CREATE TABLE journal.trades (
    id uuid PRIMARY KEY,
    action_decision_id uuid NOT NULL,
    user_id uuid NOT NULL,
    symbol text NOT NULL CHECK (length(btrim(symbol)) BETWEEN 1 AND 32),
    side text NOT NULL CHECK (side IN ('buy', 'sell')),
    quantity numeric(20,8) NOT NULL CHECK (quantity > 0),
    price numeric(20,8) NOT NULL CHECK (price > 0),
    currency char(3) NOT NULL CHECK (currency ~ '^[A-Z]{3}$'),
    executed_at timestamptz NOT NULL,
    note text CHECK (length(note) <= 2000),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    deleted_at timestamptz,
    CONSTRAINT trades_decision_owner_fk
        FOREIGN KEY (action_decision_id, user_id)
        REFERENCES journal.action_decisions(id, user_id)
);

CREATE INDEX trades_decision_idx
ON journal.trades (action_decision_id, executed_at)
WHERE deleted_at IS NULL;
