# Database migrations

## Source and naming

`platform/postgres/migrations/` is the only authoritative application-schema source. Files use `NNNN_description.sql`, four-digit contiguous IDs, and these first three lines:

```sql
-- migration-id: 0014
-- owner: journal-service
-- description: Add a nullable journal attribute
```

Legacy multi-schema files retain `platform-legacy`; new migrations normally name one service owner. `manifest.json` records exact filenames, order, and SHA-256 checksums. Never edit an applied migration or its history row. Add a new file at the end, update the manifest in review, and run both migration validators.

## Legacy filename mapping

| Former filename | Canonical filename |
|---|---|
| `001_schemas.sql` | `0001_initial_journal_performance.sql` |
| `002_identity.sql` | `0002_identity.sql` |
| `003_transactions.sql` | `0003_extend_transactions.sql` |
| `004_discipline.sql` | `0004_discipline.sql` |
| `005_reminder.sql` | `0005_reminder.sql` |
| `006_diary_deleted_events.sql` | `0006_diary_deleted_events.sql` |
| `007_market_data.sql` | `0007_market_data.sql` |
| `007_stock_research.sql` | `0008_stock_research.sql` |
| `008_price_alert.sql` | `0009_price_alert.sql` |
| `010_rotation.sql` | `0010_rotation.sql` |
| `020_phase11_12.sql` | `0011_partner_content_operations.sql` |
| `021_identity_api_keys.sql` | `0012_identity_api_keys.sql` |
| `022_journal_idempotency.sql` | `0013_journal_idempotency.sql` |

The mapping changes names and metadata only; legacy execution order and schema semantics are preserved.

## Runner guarantees

`TradeDiary.DatabaseMigrator` supports `migrate`, `status`, and explicit `baseline`. It holds one PostgreSQL session and a stable advisory lock for validation and execution. Every pending migration and its `platform_migrations.schema_history` row commit in one transaction. History records ID, description, filename, exact-byte SHA-256, timestamp, database identity, duration, release SHA, and baseline state.

The runner fails on invalid metadata, missing applied files, changed checksums, duplicate IDs, out-of-order additions, or transaction-breaking SQL. Static validation also rejects destructive automatic DDL. It never repairs history or performs a downgrade.

## Schema and role lifecycle

Database upgrades have three identities and phases:

1. The database administrator runs role bootstrap before schemas exist. It creates the fixed role allowlist, sets passwords, and grants database-level migration capability.
2. `trade_diary_migrator` alone creates the history schema and applies migrations.
3. The administrator finalizes ownership, revokes unintended access, applies service CRUD grants, cross-schema view reads from `schema-ownership.json`, and default privileges.

Runtime roles cannot create schemas, own managed objects, change migration history, or execute migrations.

## Fresh local database

`docker compose up -d --build` runs this dependency chain:

```text
postgres healthy -> db-role-bootstrap -> db-migrate -> db-role-finalize -> applications
```

PostgreSQL no longer mounts application SQL into `/docker-entrypoint-initdb.d`. A fresh volume receives every canonical migration. Any failed one-shot service prevents application startup.

## Existing local volume

An existing volume with managed schemas and no history fails closed. Do not delete it automatically. Create and verify a backup, then use the migrator's `baseline` command with the legacy fingerprint and both confirmations. The baseline executes no legacy DDL; it verifies all expected relations, important columns and indexes, stable views, the immutable timeline function/trigger, API keys, and journal idempotency before recording all 13 rows with `baseline=true`.

Inspect non-sensitive state with:

```sh
docker compose run --rm db-migrate status
```

## Kubernetes database upgrade

Forgejo builds `db-migrator:<full-commit-sha>`. Under the host namespace operation lock, release deployment verifies runtime Secrets, applies PostgreSQL infrastructure, and runs `scripts/run-k8s-database-upgrade.sh`. It waits for PostgreSQL, then executes immutable SHA-named bootstrap, migration, and finalization Jobs. Credentials enter Jobs only through `secretKeyRef`. Completed Jobs are accepted idempotently; failed Jobs and safe status remain available for diagnosis. Application images are not applied until all three Jobs complete.

## Production baseline

Provision the host lock directory first and take a verified database backup. From the same deployment host and confirmed Kubernetes context, run:

```sh
scripts/baseline-k8s-database.sh \
  --context EXPECTED_CONTEXT \
  --namespace micro-cockpit \
  --image-registry REGISTRY/PROJECT \
  --image-tag FULL_COMMIT_SHA \
  --backup-confirmed BACKUP_REFERENCE \
  --confirm-existing-database
```

This wrapper takes the same host namespace lock as deployment and rotation. The runner then takes the PostgreSQL advisory lock, requires absent/empty history, validates the complete `legacy-v1-schema.json` fingerprint, and records all legacy migrations in one transaction. It never runs automatically. A matching completed baseline rerun is a no-op; partial, fresh, unexpected, or conflicting states fail.

## Expand-first policy

Automatic pre-deployment migrations must remain compatible with the currently running release.

- Release A adds nullable/defaulted columns, tables, indexes, views, or compatible constraints.
- Release B moves reads and writes to the expanded shape.
- A later separately reviewed maintenance phase removes obsolete shape.

Dropping tables/schemas/columns, truncation, renaming live columns, incompatible type changes, destructive rewrites, and immediate old-contract removal are not accepted by this runner. Restore from a tested backup or write a reviewed forward repair after failure; never hand-edit migration history.

## Adoption status

- Implementation: completed after repository and ephemeral PostgreSQL verification.
- Production baseline: pending operator execution.
- Live Kubernetes migration Job verification: pending.
- Credential rotation: pending operator execution.
- Git history rewrite: pending.
