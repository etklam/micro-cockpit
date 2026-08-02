# Review × Watchlist UX clarification

Status: complete

## Goal

Make Review and Watchlist immediately explain their purpose, current state, and next action while preserving the existing journal, Agent ownership, and System Never Judges boundaries.

## Product flow

Today Observation → Add to Watchlist → Collect evidence over time → Create Expectation or Decision → Event occurs → Review Human and Agent reasoning.

## Delivery slices

1. Watchlist list-first onboarding and a complete add flow.
2. Watchlist item cards with real fields and actionable next steps.
3. Review guided comparison builder and Agent setup state.
4. Review result as owner-labelled responsive timelines.
5. Context-preserving links across Observation, Watchlist, Expectation, and Review.

All five slices are AFK-ready. They should use existing APIs where possible. The only currently justified backend change is allowing the existing Watchlist create operation to persist the required Watchlist Note atomically with Instrument membership.

## Deliberately deferred

- Recent comparison history, saved review runs, and reflection status: no persistence model exists.
- Semantic agreement/difference/user-only/Agent-only grouping: current API does not provide it, and system-authored interpretation would violate `System Never Judges` unless owned by a named Agent.
- Theme, Event, Hypothesis, and Custom subject Watchlist members: current identity is `instrument_id`.
- Watchlist status lifecycle, archive, next-review date, tags, validation questions, triggers, and counts: not represented by the current model/API.
- Direct Watchlist → Expectation creation: Expectations require an Observation Update; the first version should preserve context into Observation creation.
- Automatic recurring reasoning-pattern conclusions: only objective relationships or named-owner judgements are allowed.

## Verification baseline

Each slice must retain existing functionality, add or update focused frontend/API tests, preserve keyboard accessibility and responsive layouts at 1440px, 1024px, 768px, and 390px, and work in Green Light, Green Dark, Red Light, and Red Dark themes.
