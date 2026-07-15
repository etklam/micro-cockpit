-- migration-id: 0013
-- owner: platform-legacy
-- description: Journal idempotency keys

CREATE TABLE IF NOT EXISTS journal.idempotency_keys (
  user_id uuid NOT NULL,
  operation text NOT NULL,
  idempotency_key text NOT NULL CHECK (length(idempotency_key) BETWEEN 1 AND 200),
  request_hash text NOT NULL,
  status_code integer,
  location text,
  response jsonb,
  created_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (user_id, operation, idempotency_key)
);
