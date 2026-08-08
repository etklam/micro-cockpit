# Define evidence-backed Pattern trend semantics

Status: complete

Type: HITL

## What to build

Make the product decision for what a Pattern trend means before any chart or directional language is implemented. The definition must use only real reviewed Expectations, retain a visible denominator, respect Journal Day boundaries, and uphold “System Never Judges.” Record enough examples and edge-case decisions for an AFK implementation agent to proceed without inventing statistics.

## Acceptance criteria

- [x] Choose the time bucket and comparison model for weekly, monthly, and custom views.
- [x] Define the minimum number of reviewed Expectations and populated periods required before a trend may be shown.
- [x] Define whether the UI presents raw occurrences, occurrence/denominator ratios, or both.
- [x] Define neutral wording for increase, decrease, no material change, missing periods, and insufficient evidence without judging the User.
- [x] Define how timezone, Journal Day rollover, partial current periods, and zero-denominator buckets behave.
- [x] Define how users navigate from each bucket to its exact review evidence.
- [x] Add the proposed semantics to an ADR, including concrete examples and prohibited interpretations.
- [x] A maintainer explicitly approves the decision before the implementation issue begins.

## Blocked by

None - can start immediately.

## Comments

Decision accepted in `docs/adr/0008-evidence-backed-pattern-trend-semantics.md`.
