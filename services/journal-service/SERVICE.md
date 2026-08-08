# Journal service

Owns `journal.*`: Market Observations and timestamped updates, Expectations and owner reviews, reasoning labels and explicitly Confirmed Patterns, Action Decisions and optional Trade evidence, Watchlist notes, Discipline Principles, Access Grants, change cursors, and content-free deletion records.

Key invariants:

- A Market Observation belongs to one User and one Journal Day.
- Child records preserve the same owner.
- Trades are evidence, never holdings or accounting records.
- Quick Observation and selected creates support scoped idempotency keys.
- Access Grants are revocable read-only closures; they never permit editing another owner’s record.
- Human / Agent comparison reads remain owner-labeled and apply the active grant’s date and subject scope to Agent records.
- Pattern aggregation is evidence-only; confirmation and reconfirmation require at least two current owner-held reviews. Unconfirm is an explicit, reversible owner action: the Pattern, transition history, evidence links, and existing Discipline Principle provenance remain, while new sourced principles are blocked. Evidence reviews remain undeletable in confirmed, unconfirmed, and reconfirmed states.
- Deletions physically remove personal content and expose only record ID, type, version, operation, and time to incremental consumers.
- Cross-user misses are non-disclosing.

See the generated Journal OpenAPI document for the authoritative route and DTO inventory.
