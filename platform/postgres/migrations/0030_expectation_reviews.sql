-- migration-id: 0030
-- owner: journal-service
-- description: Owner-authored expectation reviews and reasoning labels

ALTER TABLE journal.expectations
    ADD CONSTRAINT expectations_id_user_unique UNIQUE (id, user_id);

CREATE TABLE journal.reasoning_labels (
    id uuid PRIMARY KEY,
    user_id uuid NOT NULL,
    kind text NOT NULL CHECK (kind IN ('issue', 'strength')),
    name text NOT NULL CHECK (length(btrim(name)) BETWEEN 1 AND 100),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    deleted_at timestamptz,
    CONSTRAINT reasoning_labels_id_user_unique UNIQUE (id, user_id)
);

CREATE UNIQUE INDEX reasoning_labels_owner_name_unique
ON journal.reasoning_labels (user_id, kind, lower(name))
WHERE deleted_at IS NULL;

CREATE TABLE journal.expectation_reviews (
    id uuid PRIMARY KEY,
    expectation_id uuid NOT NULL,
    user_id uuid NOT NULL,
    outcome text NOT NULL CHECK (outcome IN ('confirmed', 'partially_confirmed', 'invalidated', 'indeterminate')),
    reasoning_quality text NOT NULL CHECK (reasoning_quality IN ('sound', 'mixed', 'weak')),
    explanation text,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT expectation_reviews_expectation_unique UNIQUE (expectation_id),
    CONSTRAINT expectation_reviews_id_user_unique UNIQUE (id, user_id),
    CONSTRAINT expectation_reviews_owner_fk
        FOREIGN KEY (expectation_id, user_id)
        REFERENCES journal.expectations(id, user_id),
    CONSTRAINT expectation_reviews_explanation_check CHECK (
        outcome NOT IN ('partially_confirmed', 'indeterminate')
        OR length(btrim(explanation)) > 0
    )
);

CREATE TABLE journal.expectation_review_labels (
    id uuid PRIMARY KEY,
    review_id uuid NOT NULL,
    user_id uuid NOT NULL,
    kind text NOT NULL CHECK (kind IN ('issue', 'strength')),
    system_key text,
    custom_label_id uuid,
    CONSTRAINT expectation_review_labels_source_check CHECK (
        (system_key IS NOT NULL AND custom_label_id IS NULL)
        OR (system_key IS NULL AND custom_label_id IS NOT NULL)
    ),
    CONSTRAINT expectation_review_labels_review_fk
        FOREIGN KEY (review_id, user_id)
        REFERENCES journal.expectation_reviews(id, user_id)
        ON DELETE CASCADE,
    CONSTRAINT expectation_review_labels_custom_fk
        FOREIGN KEY (custom_label_id, user_id)
        REFERENCES journal.reasoning_labels(id, user_id)
);

CREATE INDEX expectation_review_labels_review_idx
ON journal.expectation_review_labels (review_id);

CREATE UNIQUE INDEX expectation_review_system_labels_unique
ON journal.expectation_review_labels (review_id, kind, system_key)
WHERE system_key IS NOT NULL;

CREATE UNIQUE INDEX expectation_review_custom_labels_unique
ON journal.expectation_review_labels (review_id, custom_label_id)
WHERE custom_label_id IS NOT NULL;
