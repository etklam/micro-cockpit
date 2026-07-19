-- migration-id: 0021
-- owner: partner-service
-- description: Human partner invitation codes and unordered-pair uniqueness for active links

CREATE TABLE IF NOT EXISTS partner.partner_invitations (
  id uuid PRIMARY KEY,
  creator_user_id uuid NOT NULL,
  code_hash bytea NOT NULL,
  status text NOT NULL CHECK (status IN ('pending', 'redeemed', 'revoked')),
  expires_at timestamptz NOT NULL,
  redeemed_at timestamptz,
  redeemed_by_user_id uuid,
  created_link_id uuid REFERENCES partner.partner_links(id),
  created_at timestamptz NOT NULL DEFAULT now(),
  revoked_at timestamptz,
  CONSTRAINT partner_invitations_code_hash_unique UNIQUE (code_hash),
  CONSTRAINT partner_invitations_redeemed_shape CHECK (
    (status = 'redeemed' AND redeemed_at IS NOT NULL AND redeemed_by_user_id IS NOT NULL AND created_link_id IS NOT NULL)
    OR (status <> 'redeemed' AND redeemed_at IS NULL AND redeemed_by_user_id IS NULL AND created_link_id IS NULL)
  ),
  CONSTRAINT partner_invitations_revoked_shape CHECK (
    (status = 'revoked' AND revoked_at IS NOT NULL)
    OR (status <> 'revoked' AND revoked_at IS NULL)
  )
);

CREATE INDEX IF NOT EXISTS partner_invitations_creator_pending_idx
  ON partner.partner_invitations (creator_user_id, created_at DESC)
  WHERE status = 'pending';

-- One pending/accepted relationship per unordered human pair (legacy UUID invites + code redemption).
CREATE UNIQUE INDEX IF NOT EXISTS partner_links_active_pair_uidx
  ON partner.partner_links (
    LEAST(requester_user_id, partner_user_id),
    GREATEST(requester_user_id, partner_user_id)
  )
  WHERE status IN ('pending', 'accepted');
