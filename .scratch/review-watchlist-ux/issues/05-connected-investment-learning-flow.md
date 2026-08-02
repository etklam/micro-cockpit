# 05 — Connect Observation, Watchlist, Expectation, and Review context

Status: complete

## What to build

Complete the supported cross-feature journey so users can move from a Today Observation to continuing Watchlist evidence and then into Review without losing Instrument or date context. Reuse existing routes and mutations, and make only links or prefilled navigation changes that the current domain model can support. Do not create duplicate records or pretend that an Instrument alone can create an Expectation without an Observation Update.

## Acceptance criteria

- [ ] An Observation Update with a supported Instrument subject can add that Instrument to the user’s Watchlist through the existing membership flow, with duplicate membership handled safely.
- [ ] A Watchlist item can open the Instrument observation timeline and the supported new-observation path with the Instrument context preserved.
- [ ] A completed or reviewable Expectation can open Review with its supported target/date context prefilled, while the user can still change the scope before submitting.
- [ ] Returning from AI Agent setup restores the pending comparison target, period, and Agent selection when the route/state mechanism supports it; otherwise the user receives a clear recovery action without data loss.
- [ ] Navigation is keyboard accessible, does not duplicate domain records, and tests cover each supported link, query/context preservation, duplicate handling, and unsupported-path messaging.

## Blocked by

- 01-watchlist-list-first-onboarding.md
- 03-review-guided-comparison-builder.md
