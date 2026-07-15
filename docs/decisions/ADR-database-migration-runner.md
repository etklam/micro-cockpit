# ADR: Centralized deployment-time database migration runner

- Status: Accepted
- Scope: PostgreSQL schema deployment

## Context

Micro Cockpit services are independently replaceable, but currently share one physical PostgreSQL database with service-owned schemas. Releases can contain ordered changes across several schemas, and runtime services must not race to mutate the database or carry administrator capabilities.

## Decision

Use one deployment-time migration runner and one immutable ordered migration ledger for the shared database. Every migration declares its service owner in metadata. The owning service remains responsible for schema design, compatibility, review, and tests. Runtime services never execute migrations.

The runner is deployment infrastructure. It does not expose shared entities, a shared DbContext, repositories, or domain behavior to runtime services. It provides deterministic cross-schema ordering, checksum and Git-history immutability, one advisory lock, and one release gate.

Future service rewrites append migrations to the ledger. They do not modify historical files or introduce a second history for the same physical database.

## Consequences

- Cross-schema release ordering is deterministic.
- Deployment stops before application rollout when database work fails.
- Service ownership remains visible and reviewable in migration metadata.
- Independently replaceable services do not depend on shared runtime domain code.
- The shared physical database has one authoritative migration state rather than conflicting per-service histories.
