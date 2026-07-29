-- migration-id: 0032
-- owner: journal-service
-- description: User-owned Instrument watchlist and short notes

CREATE TABLE journal.watchlist_items (
    user_id uuid NOT NULL,
    instrument_id uuid NOT NULL,
    note text CHECK (note IS NULL OR length(note) <= 500),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (user_id, instrument_id)
);

CREATE INDEX watchlist_items_instrument_idx
ON journal.watchlist_items (instrument_id);
