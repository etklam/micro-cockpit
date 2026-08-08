-- migration-id: 0045
-- owner: journal-service
-- description: Reversible confirmed-pattern state with durable transition history

ALTER TABLE journal.confirmed_patterns
    ADD COLUMN is_confirmed boolean NOT NULL DEFAULT true,
    ADD COLUMN confirmed_at timestamptz NOT NULL DEFAULT now(),
    ADD COLUMN unconfirmed_at timestamptz,
    ADD COLUMN updated_at timestamptz NOT NULL DEFAULT now(),
    ADD CONSTRAINT confirmed_patterns_state_check CHECK (
        (is_confirmed AND unconfirmed_at IS NULL)
        OR (NOT is_confirmed AND unconfirmed_at IS NOT NULL)
    );

UPDATE journal.confirmed_patterns
SET confirmed_at=created_at,updated_at=created_at;

CREATE TABLE journal.confirmed_pattern_state_events (
    id uuid PRIMARY KEY,
    confirmed_pattern_id uuid NOT NULL,
    user_id uuid NOT NULL,
    state text NOT NULL CHECK (state IN ('confirmed', 'unconfirmed')),
    occurred_at timestamptz NOT NULL,
    CONSTRAINT confirmed_pattern_state_events_pattern_owner_fk
        FOREIGN KEY (confirmed_pattern_id, user_id)
        REFERENCES journal.confirmed_patterns(id, user_id)
        ON DELETE CASCADE
);

CREATE INDEX confirmed_pattern_state_events_pattern_time_idx
ON journal.confirmed_pattern_state_events (confirmed_pattern_id, occurred_at, id);

INSERT INTO journal.confirmed_pattern_state_events(id,confirmed_pattern_id,user_id,state,occurred_at)
SELECT gen_random_uuid(),id,user_id,'confirmed',created_at
FROM journal.confirmed_patterns;

CREATE FUNCTION journal.record_confirmed_pattern_state_event() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP='INSERT' OR NEW.is_confirmed IS DISTINCT FROM OLD.is_confirmed THEN
        INSERT INTO journal.confirmed_pattern_state_events(id,confirmed_pattern_id,user_id,state,occurred_at)
        VALUES(
            gen_random_uuid(),NEW.id,NEW.user_id,
            CASE WHEN NEW.is_confirmed THEN 'confirmed' ELSE 'unconfirmed' END,
            CASE WHEN NEW.is_confirmed THEN NEW.confirmed_at ELSE NEW.unconfirmed_at END
        );
    END IF;
    RETURN NEW;
END;
$$;

CREATE TRIGGER confirmed_pattern_state_event
AFTER INSERT OR UPDATE OF is_confirmed ON journal.confirmed_patterns
FOR EACH ROW EXECUTE FUNCTION journal.record_confirmed_pattern_state_event();
