# Add a reliable direct Guided Review context read model

Status: complete

Type: AFK

## What to build

Provide an owner-scoped read path that loads the exact retained context needed to review one Expectation without searching paginated observation history. Guided Review must reliably show the source Observation Update, Signal, Interpretation, relevant evidence, Action Decisions, executions, and Trades even when the Journal Day contains more records than a normal history page.

## Acceptance criteria

- [x] Guided Review obtains context by exact Expectation identity rather than scanning already-loaded history pages.
- [x] The response preserves the ownership boundaries between Expectation, Observation Update, Observation, Action Decision, and Trade evidence.
- [x] Cross-user and inaccessible records return the repository-standard non-disclosing `404`.
- [x] Deleted or unavailable optional context produces an explicit partial-context state; it does not silently display unrelated records.
- [x] The read model remains self-review context and does not merge Human and Agent-owned judgement.
- [x] The frontend renders the same calm read-only hierarchy and does not add a second competing review workflow.
- [x] Integration and frontend tests prove the exact context is found when the target update lies beyond the first history page.
- [x] Runtime DTOs, service and Edge OpenAPI, and the generated frontend client are regenerated and verified.

## Blocked by

None - can start immediately.

## Comments

Completed with an owner-scoped direct read model, explicit partial-context state, generated Edge client wrapper, and full backend/frontend verification.
