# Backend architecture

## Active processes

| Process | Boundary |
|---|---|
| Edge | browser API, auth cookie boundary, proxying, Bootstrap and Calendar composition |
| Identity | humans, Agent Users, access/refresh tokens, API tokens, settings |
| Journal | observation, expectation, review, decision, Watchlist, grants, sync |
| Market Data | Instruments, symbol history, raw and adjusted Daily Close |
| Tool | deterministic calculations, presets, Calculation Snapshots |

Simple Edge routes use `EdgeTransport.MapProxy`. It forwards correlation, authorization, and idempotency headers, bounds downstream time, and maps transport failures to safe ProblemDetails. Composed routes deserialize typed DTOs.

Human sessions and Agent tokens carry an account type and scopes. Edge policies reject Agent writes without the exact Journal scope, while each service still applies owner predicates. Access Grant reads are implemented in Journal so filters cannot bypass the grant closure.

Personal-content deletion is physical. Incremental sync emits content only for live changes and ID/type/time-only tombstones for deletions. Immutable events never retain personal text.

Persistence changes are append-only migrations with checksums, except the explicitly approved unlaunched-product cutover migration `0041`.
