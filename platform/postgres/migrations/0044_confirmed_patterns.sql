-- migration-id: 0044
-- owner: journal-service
-- description: User-confirmed reasoning patterns and discipline provenance

CREATE TABLE journal.confirmed_patterns (
    id uuid PRIMARY KEY,
    user_id uuid NOT NULL,
    kind text NOT NULL CHECK (kind IN ('issue', 'strength')),
    label_key text NOT NULL CHECK (length(btrim(label_key)) BETWEEN 1 AND 100),
    label_name text NOT NULL CHECK (length(btrim(label_name)) BETWEEN 1 AND 100),
    is_system boolean NOT NULL,
    custom_label_id uuid,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT confirmed_patterns_id_user_unique UNIQUE (id, user_id),
    CONSTRAINT confirmed_patterns_source_check CHECK (is_system = (custom_label_id IS NULL)),
    CONSTRAINT confirmed_patterns_custom_label_owner_fk
        FOREIGN KEY (custom_label_id, user_id)
        REFERENCES journal.reasoning_labels(id, user_id),
    CONSTRAINT confirmed_patterns_owner_label_unique UNIQUE (user_id, kind, label_key)
);

CREATE INDEX confirmed_patterns_owner_created_idx
ON journal.confirmed_patterns (user_id, created_at DESC, id);

CREATE TABLE journal.confirmed_pattern_evidence (
    confirmed_pattern_id uuid NOT NULL,
    user_id uuid NOT NULL,
    review_id uuid NOT NULL,
    PRIMARY KEY (confirmed_pattern_id, review_id),
    CONSTRAINT confirmed_pattern_evidence_pattern_owner_fk
        FOREIGN KEY (confirmed_pattern_id, user_id)
        REFERENCES journal.confirmed_patterns(id, user_id)
        ON DELETE CASCADE,
    CONSTRAINT confirmed_pattern_evidence_review_owner_fk
        FOREIGN KEY (review_id, user_id)
        REFERENCES journal.expectation_reviews(id, user_id)
);

CREATE INDEX confirmed_pattern_evidence_review_idx
ON journal.confirmed_pattern_evidence (review_id, user_id);

ALTER TABLE journal.discipline_principles
    ADD COLUMN confirmed_pattern_id uuid,
    ADD CONSTRAINT discipline_principles_confirmed_pattern_owner_fk
        FOREIGN KEY (confirmed_pattern_id, user_id)
        REFERENCES journal.confirmed_patterns(id, user_id);
