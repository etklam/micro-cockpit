-- migration-id: 0033
-- owner: journal-service
-- description: Manually managed and selected discipline principles

CREATE TABLE journal.discipline_principles (
    id uuid PRIMARY KEY,
    user_id uuid NOT NULL,
    content text NOT NULL CHECK (length(trim(content)) BETWEEN 1 AND 280),
    status text NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'disabled', 'archived')),
    selected_for_today boolean NOT NULL DEFAULT false,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT discipline_principles_id_user_unique UNIQUE (id, user_id),
    CONSTRAINT discipline_principles_selected_active_check
        CHECK (NOT selected_for_today OR status = 'active')
);

CREATE UNIQUE INDEX discipline_principles_one_selected_per_user
ON journal.discipline_principles (user_id)
WHERE selected_for_today;

CREATE INDEX discipline_principles_user_status_idx
ON journal.discipline_principles (user_id, status, created_at DESC);
