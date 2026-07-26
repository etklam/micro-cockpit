-- migration-id: 0027
-- owner: market-data-service
-- description: Stable Instrument identity with symbol history

CREATE TABLE market.instruments (
    id uuid PRIMARY KEY,
    created_at timestamptz NOT NULL DEFAULT now()
);

ALTER TABLE market.symbols
    ADD COLUMN instrument_id uuid NOT NULL DEFAULT gen_random_uuid();

INSERT INTO market.instruments (id)
SELECT instrument_id FROM market.symbols;

ALTER TABLE market.symbols
    ADD CONSTRAINT market_symbols_instrument_fk FOREIGN KEY (instrument_id) REFERENCES market.instruments(id);

CREATE FUNCTION market.ensure_instrument_identity() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    INSERT INTO market.instruments(id) VALUES (NEW.instrument_id) ON CONFLICT DO NOTHING;
    RETURN NEW;
END;
$$;

CREATE TRIGGER market_symbols_ensure_instrument
BEFORE INSERT ON market.symbols
FOR EACH ROW EXECUTE FUNCTION market.ensure_instrument_identity();

CREATE UNIQUE INDEX market_symbols_active_instrument_uidx
ON market.symbols (instrument_id)
WHERE active;

CREATE TABLE market.instrument_symbol_history (
    instrument_id uuid NOT NULL REFERENCES market.instruments(id),
    symbol text NOT NULL,
    valid_from timestamptz NOT NULL DEFAULT now(),
    valid_to timestamptz,
    PRIMARY KEY (instrument_id, symbol, valid_from),
    CHECK (valid_to IS NULL OR valid_to > valid_from)
);

INSERT INTO market.instrument_symbol_history (instrument_id, symbol, valid_from, valid_to)
SELECT instrument_id, symbol, '-infinity'::timestamptz,
       CASE WHEN active THEN NULL ELSE updated_at END
FROM market.symbols;

DROP VIEW market.published_symbols_v1;
CREATE VIEW market.published_symbols_v1 AS
SELECT instrument_id, symbol, name, exchange, currency, timezone, active, updated_at
FROM market.symbols
WHERE active;
