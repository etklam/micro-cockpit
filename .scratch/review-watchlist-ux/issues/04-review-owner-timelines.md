# 04 — Review result as owner-labelled responsive timelines

Status: complete

## What to build

Present the existing comparison response as a clear result with target, period, Agent, and a way to change scope, followed by objective summary values and Human/AI Agent timelines. Keep ownership and read-only boundaries explicit. Use the API’s actual observations, Expectations, outcomes, confidence, and Daily Close evidence; do not fabricate agreement groups, counts, summaries, or system judgements that the endpoint does not return.

## Acceptance criteria

- [ ] The result header identifies the selected target, comparison period, and AI Agent and provides an accessible action to change the comparison scope.
- [ ] Summary metrics render only when calculable from the response; unavailable values are labelled unavailable rather than guessed.
- [ ] Wide screens show separate Human and AI Agent columns; narrow screens use stacked or tabbed sections without horizontal scrolling.
- [ ] Every record retains its original timestamp/journal day, content, owner label, subject/evidence link when available, and associated Expectation fields; neither side can be edited from this view.
- [ ] Empty Human/Agent, grant-unavailable, API error, and loading states are distinguishable and explain the next action; copy states that the system does not decide who is right.
- [ ] Tests cover successful rendering, owner labels, unavailable/empty data, retry, read-only behavior, keyboard access, and 390px/1440px layouts in all four theme combinations.

## Blocked by

- 03-review-guided-comparison-builder.md
