# Implement evidence-backed Pattern trends end to end

Status: complete

Type: AFK

## What to build

Implement the approved Pattern trend semantics as a complete Journal-to-Edge-to-frontend slice. Trend points must be calculated from real owner-held reviews, expose their denominators and evidence, and disappear into a clear insufficient-evidence state whenever the approved support threshold is not met.

## Acceptance criteria

- [x] Journal produces canonical trend buckets from real reviewed Expectations according to the approved semantics.
- [x] Every bucket includes the occurrence count, reviewed-Expectation denominator, date window, and navigable source evidence required by the decision.
- [x] Trend aggregation preserves User ownership and cannot include another User's reviews or Agent-authored judgement.
- [x] Insufficient or missing data returns an explicit supported state; the service never fabricates zeroes or directional claims.
- [x] Pattern detail presents the trend with calm, non-judgmental language and remains usable without relying on color alone.
- [x] Weekly, monthly, custom, timezone, partial-period, zero-denominator, and cross-user cases have endpoint/integration coverage.
- [x] Frontend tests cover supported trends, insufficient evidence, evidence navigation, loading, and error states.
- [x] Runtime DTOs, Journal and Edge OpenAPI, generated client, migration validation if applicable, and architecture checks all pass.

## Blocked by

- 04 — Define evidence-backed Pattern trend semantics.

## Comments

Completed against accepted ADR 0008; canonical trend payload, neutral UI, exact bucket evidence, contracts, and all verification gates pass.
