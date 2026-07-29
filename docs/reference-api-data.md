# API and data conventions

Generated OpenAPI is authoritative. The frontend regenerates `src/generated/edge.ts` from the Edge document and never edits it manually.

Requests carry `Authorization: Bearer`, correlation IDs, and an optional `Idempotency-Key` on supported creates. Edge policies distinguish human sessions, Journal read/write scopes, Agent read scope, and admin-only operations. Services still enforce owner predicates.

Dates use ISO local dates; instants use UTC ISO 8601. Journal Day is resolved from the owner timezone and rollover. Money always carries currency. Missing evidence is `null` or an explicit unavailable status, never a fabricated zero.

Market Observation search filters are composable and cursor-paginated. Stable Instrument IDs are separate from symbols. Daily Close responses distinguish raw and adjusted values and use completed published sessions only.

Access Grant full sync returns records plus `syncCursor`. Incremental sync returns live content for changes and `{recordId, recordType, deletedAt}` for deletions. Cursors are opaque and bounded by retention.

`RecordDeleted.v1` is content-free and restricted to Market Observation closure record types. The former Diary deletion pipeline is retired and its persistence content-scrubbed.

See [application API](api/application-api.md), [schema](database/schema.md), and [`contracts/openapi`](../contracts/openapi).
