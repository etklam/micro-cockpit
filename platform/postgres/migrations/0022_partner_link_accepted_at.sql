-- migration-id: 0022
-- owner: partner-service
-- description: Append-only accepted_at on partner links (set once on accept/redeem)

ALTER TABLE partner.partner_links
  ADD COLUMN IF NOT EXISTS accepted_at timestamptz;

-- Backfill accepted links that predate this column; leave pending/revoked/declined null.
UPDATE partner.partner_links
SET accepted_at = coalesce(updated_at, created_at)
WHERE status = 'accepted' AND accepted_at IS NULL;
