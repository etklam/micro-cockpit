-- migration-id: 0035
-- owner: identity-service
-- description: Agent management ownership and single active API Token lifecycle

ALTER TABLE identity.api_keys
    ALTER COLUMN created_by DROP NOT NULL,
    ADD COLUMN last_used_at timestamptz,
    ADD COLUMN last_successful_request_at timestamptz;

CREATE TABLE identity.agent_managers (
    agent_user_id uuid PRIMARY KEY REFERENCES identity.users(id),
    manager_type text NOT NULL CHECK (manager_type IN ('human', 'platform')),
    manager_user_id uuid REFERENCES identity.users(id),
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT agent_managers_owner_shape CHECK (
        (manager_type='human' AND manager_user_id IS NOT NULL)
        OR (manager_type='platform' AND manager_user_id IS NULL)
    )
);

CREATE INDEX agent_managers_human_idx
ON identity.agent_managers (manager_user_id)
WHERE manager_type='human';

UPDATE identity.api_keys older
SET revoked_at=now()
WHERE revoked_at IS NULL
  AND EXISTS (
      SELECT 1 FROM identity.api_keys newer
      WHERE newer.user_id=older.user_id AND newer.revoked_at IS NULL
        AND (newer.created_at,newer.id)>(older.created_at,older.id)
  );

CREATE UNIQUE INDEX api_keys_one_active_per_user
ON identity.api_keys (user_id)
WHERE revoked_at IS NULL;
