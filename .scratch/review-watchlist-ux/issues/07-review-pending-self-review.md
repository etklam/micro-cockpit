# 07 — Review pending queue and AI-independent self-review

Status: ready-for-human

Type: HITL

## What to build

Make the primary Review path a user’s own retrospective evaluation of historical Expectations. The default Review view should show real Expectations that are ready or eligible for review, let the user open a guided self-review without selecting or creating an AI Agent, and save the review before any optional AI analysis. Keep Human × Agent comparison available only as a secondary or deferred path. Before implementation, decide whether the current review fields and available historical data are sufficient or whether the smallest backend extension is required; do not fabricate immutable snapshots, evidence, or reasoning fields that the API cannot provide.

## Acceptance criteria

- [ ] Review opens on a default “待回顧” experience rather than the Human × Agent comparison builder; the landing page does not require Subject Type, Subject, date range, or Agent selection before showing reviewable Expectations.
- [ ] The pending queue uses the existing readiness/review domain logic and renders only real target, expectation summary, created date, review date, confidence, relationship, and review-status values; no readiness state is invented.
- [ ] When no Expectations are ready, the page shows a compact empty state with links only to real routes for viewing Expectations or creating a new Expectation.
- [ ] “開始回顧” opens a guided review view without querying for or requiring an AI Agent. The original Expectation context is read-only and preserves available historical values; any missing immutable-snapshot capability is documented rather than silently replaced by current content.
- [ ] The user can select all supported outcome classifications, complete the available reasoning-review fields, receive required-field validation, and save the Review through the established query/mutation layer.
- [ ] Completing a Review does not require saving a Discipline Principle; when the existing principle collection is supported, extraction is optional and remains within the same completion flow.
- [ ] The saved self-review becomes the source for the completed state before any AI feedback is requested; optional AI analysis is collapsed/secondary and cannot overwrite the user’s Review.
- [ ] Existing Pattern Analysis, Discipline, comparison, loading, error, retry, and navigation behavior remains accessible while the comparison builder no longer dominates the primary workflow.
- [ ] Tests cover the pending list and empty state, starting a Review, original-context rendering, all supported outcomes, validation, save/completion with and without a principle, completion without an AI Agent, optional AI setup, keyboard behavior, and 1440px/1024px/768px/390px layouts.
- [ ] Verification notes report the previous and new Review flows, files changed, APIs reused, backend or snapshot limitations, test results, Green Dark screenshots, and deferred AI comparison work.

## Blocked by

None - can start immediately.
