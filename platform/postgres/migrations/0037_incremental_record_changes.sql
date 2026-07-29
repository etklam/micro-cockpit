-- migration-id: 0037
-- owner: journal-service
-- description: Content-free record change feed, deletion events, and optional delegate source labels

ALTER TABLE journal.observation_updates
    ADD COLUMN source_label text CHECK (length(btrim(source_label)) BETWEEN 1 AND 100);

ALTER TABLE journal.expectation_reviews
    ADD COLUMN deleted_at timestamptz;

CREATE TABLE journal.record_changes (
    sequence bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    record_id uuid NOT NULL,
    record_type text NOT NULL CHECK (record_type IN (
        'market_observation', 'observation_update', 'expectation',
        'expectation_review', 'action_decision', 'trade'
    )),
    owner_user_id uuid NOT NULL,
    market_observation_id uuid NOT NULL,
    operation text NOT NULL CHECK (operation IN ('upsert', 'deleted')),
    changed_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX record_changes_sync_idx
ON journal.record_changes (changed_at, sequence);

-- No JSON payload by design: this immutable table cannot store personal record content.
CREATE TABLE journal.record_outbox_events (
    event_id uuid PRIMARY KEY,
    record_id uuid NOT NULL,
    record_type text NOT NULL CHECK (record_type IN (
        'market_observation', 'observation_update', 'expectation',
        'expectation_review', 'action_decision', 'trade'
    )),
    event_version integer NOT NULL CHECK (event_version = 1),
    operation text NOT NULL CHECK (operation = 'deleted'),
    event_time timestamptz NOT NULL,
    published_at timestamptz
);

CREATE INDEX record_outbox_unpublished_idx
ON journal.record_outbox_events (event_time)
WHERE published_at IS NULL;

CREATE FUNCTION journal.capture_record_change() RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    changed_record_id uuid;
    changed_owner_id uuid;
    changed_observation_id uuid;
    changed_operation text;
    changed_time timestamptz;
BEGIN
    changed_record_id := CASE WHEN TG_OP='DELETE' THEN OLD.id ELSE NEW.id END;
    changed_owner_id := CASE WHEN TG_OP='DELETE' THEN OLD.user_id ELSE NEW.user_id END;
    changed_time := CASE WHEN TG_OP='DELETE' THEN now() ELSE COALESCE(NEW.deleted_at,NEW.updated_at,now()) END;
    changed_operation := CASE
        WHEN TG_OP='DELETE' OR (TG_OP='UPDATE' AND NEW.deleted_at IS NOT NULL AND OLD.deleted_at IS NULL)
            THEN 'deleted'
        ELSE 'upsert'
    END;

    IF TG_TABLE_NAME='market_observations' THEN
        changed_observation_id := changed_record_id;
    ELSIF TG_TABLE_NAME='observation_updates' THEN
        changed_observation_id := CASE WHEN TG_OP='DELETE' THEN OLD.market_observation_id ELSE NEW.market_observation_id END;
    ELSIF TG_TABLE_NAME='expectations' THEN
        SELECT market_observation_id INTO changed_observation_id
        FROM journal.observation_updates
        WHERE id=CASE WHEN TG_OP='DELETE' THEN OLD.observation_update_id ELSE NEW.observation_update_id END;
    ELSIF TG_TABLE_NAME='expectation_reviews' THEN
        SELECT u.market_observation_id INTO changed_observation_id
        FROM journal.expectations e
        JOIN journal.observation_updates u ON u.id=e.observation_update_id
        WHERE e.id=CASE WHEN TG_OP='DELETE' THEN OLD.expectation_id ELSE NEW.expectation_id END;
    ELSIF TG_TABLE_NAME='action_decisions' THEN
        SELECT market_observation_id INTO changed_observation_id
        FROM journal.observation_updates
        WHERE id=CASE WHEN TG_OP='DELETE' THEN OLD.observation_update_id ELSE NEW.observation_update_id END;
    ELSIF TG_TABLE_NAME='trades' THEN
        SELECT u.market_observation_id INTO changed_observation_id
        FROM journal.action_decisions a
        JOIN journal.observation_updates u ON u.id=a.observation_update_id
        WHERE a.id=CASE WHEN TG_OP='DELETE' THEN OLD.action_decision_id ELSE NEW.action_decision_id END;
    END IF;

    IF changed_observation_id IS NOT NULL THEN
        INSERT INTO journal.record_changes(
            record_id,record_type,owner_user_id,market_observation_id,operation,changed_at
        ) VALUES(
            changed_record_id,
            CASE TG_TABLE_NAME
                WHEN 'market_observations' THEN 'market_observation'
                WHEN 'observation_updates' THEN 'observation_update'
                WHEN 'expectations' THEN 'expectation'
                WHEN 'expectation_reviews' THEN 'expectation_review'
                WHEN 'action_decisions' THEN 'action_decision'
                ELSE 'trade'
            END,
            changed_owner_id,changed_observation_id,changed_operation,changed_time
        );
        IF changed_operation='deleted' THEN
            INSERT INTO journal.record_outbox_events(
                event_id,record_id,record_type,event_version,operation,event_time
            ) VALUES(
                gen_random_uuid(),changed_record_id,
                CASE TG_TABLE_NAME
                    WHEN 'market_observations' THEN 'market_observation'
                    WHEN 'observation_updates' THEN 'observation_update'
                    WHEN 'expectations' THEN 'expectation'
                    WHEN 'expectation_reviews' THEN 'expectation_review'
                    WHEN 'action_decisions' THEN 'action_decision'
                    ELSE 'trade'
                END,
                1,'deleted',changed_time
            );
        END IF;
    END IF;
    RETURN CASE WHEN TG_OP='DELETE' THEN OLD ELSE NEW END;
END
$$;

CREATE TRIGGER market_observations_record_change
AFTER INSERT OR UPDATE OR DELETE ON journal.market_observations
FOR EACH ROW EXECUTE FUNCTION journal.capture_record_change();
CREATE TRIGGER observation_updates_record_change
AFTER INSERT OR UPDATE OR DELETE ON journal.observation_updates
FOR EACH ROW EXECUTE FUNCTION journal.capture_record_change();
CREATE TRIGGER expectations_record_change
AFTER INSERT OR UPDATE OR DELETE ON journal.expectations
FOR EACH ROW EXECUTE FUNCTION journal.capture_record_change();
CREATE TRIGGER expectation_reviews_record_change
AFTER INSERT OR UPDATE OR DELETE ON journal.expectation_reviews
FOR EACH ROW EXECUTE FUNCTION journal.capture_record_change();
CREATE TRIGGER action_decisions_record_change
AFTER INSERT OR UPDATE OR DELETE ON journal.action_decisions
FOR EACH ROW EXECUTE FUNCTION journal.capture_record_change();
CREATE TRIGGER trades_record_change
AFTER INSERT OR UPDATE OR DELETE ON journal.trades
FOR EACH ROW EXECUTE FUNCTION journal.capture_record_change();
