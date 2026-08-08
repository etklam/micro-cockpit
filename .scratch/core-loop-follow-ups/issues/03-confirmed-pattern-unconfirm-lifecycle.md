# Add a safe Confirmed Pattern unconfirm lifecycle

Status: complete

Type: AFK

## What to build

Let a User explicitly mark a Confirmed Pattern as no longer confirmed without destroying its historical evidence or breaking Discipline Principle provenance. Existing principles must continue to explain which historical pattern produced them, while future Pattern Review and principle-creation behavior reflects the pattern's current confirmation state.

## Acceptance criteria

- [x] Unconfirm is an explicit owner action; review edits never automatically confirm or unconfirm a Pattern.
- [x] Unconfirm preserves the Pattern record, exact source-review relationships, confirmation history, and existing Discipline Principle provenance.
- [x] An unconfirmed Pattern is visibly distinguished from a currently confirmed Pattern and cannot create a new sourced Principle until reconfirmed.
- [x] Reconfirm requires the normal recurring-evidence invariant, is idempotent, and records any newly qualifying source reviews without duplicating old evidence.
- [x] Cross-user unconfirm and reconfirm attempts return non-disclosing `404` responses.
- [x] Account export represents current state and relevant lifecycle timestamps; account deletion still removes all personal content in FK-safe order.
- [x] Review deletion behavior is documented and tested for confirmed, unconfirmed, and reconfirmed evidence states.
- [x] Schema migration, Journal and Edge APIs, UI controls, query invalidation, integration tests, and frontend tests deliver one complete workflow.
- [x] Runtime OpenAPI and the generated frontend client have no drift.

## Blocked by

None - can start immediately.

## Comments

Completed with migration 0045, durable transition history, owner-scoped unconfirm/reconfirm, preserved Principle provenance, and full contract/test verification.
