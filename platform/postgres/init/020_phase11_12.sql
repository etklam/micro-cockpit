CREATE SCHEMA IF NOT EXISTS partner;
CREATE TABLE IF NOT EXISTS partner.partner_links (
  id uuid PRIMARY KEY, requester_user_id uuid NOT NULL, partner_user_id uuid NOT NULL,
  partner_type text NOT NULL CHECK(partner_type IN ('human','agent')),
  status text NOT NULL CHECK(status IN ('pending','accepted','declined','revoked')),
  created_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL DEFAULT now(),
  CHECK(requester_user_id <> partner_user_id), UNIQUE(requester_user_id,partner_user_id)
);
CREATE TABLE IF NOT EXISTS partner.partner_share_policies (
  link_id uuid NOT NULL REFERENCES partner.partner_links(id) ON DELETE CASCADE,
  owner_user_id uuid NOT NULL, share_diaries boolean NOT NULL DEFAULT false,
  share_transactions boolean NOT NULL DEFAULT false, share_performance boolean NOT NULL DEFAULT false,
  updated_at timestamptz NOT NULL DEFAULT now(), PRIMARY KEY(link_id,owner_user_id)
);

CREATE SCHEMA IF NOT EXISTS content;
CREATE TABLE IF NOT EXISTS content.posts (
  id uuid PRIMARY KEY, author_user_id uuid NOT NULL, slug text NOT NULL UNIQUE,
  title text NOT NULL, body text NOT NULL, status text NOT NULL CHECK(status IN ('draft','published','archived')),
  published_at timestamptz, created_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS content.post_tags(post_id uuid NOT NULL REFERENCES content.posts(id) ON DELETE CASCADE, tag text NOT NULL, PRIMARY KEY(post_id,tag));

CREATE SCHEMA IF NOT EXISTS operations;
CREATE TABLE IF NOT EXISTS operations.audit_events (
  id uuid PRIMARY KEY, actor_user_id uuid, action text NOT NULL, resource_type text NOT NULL,
  resource_id text, details jsonb NOT NULL DEFAULT '{}', occurred_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS operations.job_registry (
  id uuid PRIMARY KEY, job_type text NOT NULL, status text NOT NULL CHECK(status IN ('queued','running','succeeded','failed')),
  requested_by uuid NOT NULL, payload jsonb NOT NULL DEFAULT '{}', result jsonb,
  created_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS operations.service_health_history (
  id uuid PRIMARY KEY, service_name text NOT NULL, status text NOT NULL, checked_at timestamptz NOT NULL DEFAULT now()
);
