# ADR 0002: No cross-schema writes

Status: accepted

A service writes only its owned schema. Foreign keys, triggers, functions, or transactions may not mutate another service's schema. Required synchronous validation uses the owner's API; asynchronous reactions use outbox/inbox events. Published views are read-only.

This prevents hidden coupling and lets each owner migrate and restore independently. The architecture verifier catches obvious DML violations; PostgreSQL roles provide the authoritative runtime control.
