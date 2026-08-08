# Evidence-backed Pattern trend semantics

## Status

Accepted

## Context

Pattern Review may show objective changes in the frequency of a User-owned Reasoning Issue or Reasoning Strength. It must not turn those changes into a diagnosis, score, recommendation, or claim that the User improved or deteriorated. Every value must remain traceable to the reviewed Expectations that produced it and must use the owner's Journal Day timezone and rollover.

## Decision

### Comparison windows

A trend compares two adjacent windows with the same number of inclusive Journal Days:

- **Weekly:** the current rolling seven Journal Days, ending on the current Journal Day, compared with the immediately preceding seven Journal Days.
- **Monthly:** the current calendar month through the current Journal Day, compared with the same number of immediately preceding Journal Days. The comparison is not labelled “previous month” unless the current month is complete and both windows happen to align to calendar months.
- **Custom:** the selected inclusive range, compared with the immediately preceding range of the same length.

All boundaries use the owner's authoritative timezone and rollover. Each local window is converted to a half-open UTC interval: `[first Journal Day rollover, day-after-last rollover)`. DST gaps and folds use the same boundary rules as Today and Calendar. A current period may be partial; the UI labels it “through {Journal Day}” and never extrapolates it.

Custom ranges remain limited to 366 Journal Days. If the preceding equal-length window would underflow the supported date domain, no trend is produced.

### Values and support threshold

For each label and each window, calculate:

- `occurrenceCount`: distinct owner-held, non-deleted Expectation Reviews carrying that label;
- `reviewedExpectationCount`: all distinct owner-held, non-deleted Expectation Reviews in the window;
- `observedShare`: `occurrenceCount / reviewedExpectationCount`.

The UI always presents the raw relationship first: “3 of 10 reviewed Expectations (30.0%).” A percentage never appears without its count and denominator.

A directional comparison is supported only when:

- both windows contain at least 5 reviewed Expectations; and
- at least one occurrence exists across the two windows.

Two populated windows are required. A zero-denominator window, a missing window, or a window below the threshold produces `insufficient_evidence`; it is not treated as zero occurrence and no direction is shown.

Compare shares exactly by cross multiplication (`currentCount * previousDenominator` versus `previousCount * currentDenominator`). Display percentages rounded to one decimal place, but never use rounded values to choose direction.

### Neutral language

Allowed wording:

- Higher: “The observed share was higher in this period: 3 of 10, compared with 1 of 10.”
- Lower: “The observed share was lower in this period: 1 of 10, compared with 3 of 10.”
- Equal: “The observed share was the same in both periods: 2 of 10 and 1 of 5.”
- Insufficient: “Not enough reviewed Expectations in both periods to compare.”
- Missing/zero denominator: “No reviewed Expectations in one of the comparison periods.”

“No material change” means only an exactly equal observed share. The product does not invent a tolerance or statistical-significance threshold. If a future design needs either, it requires a new decision with an explicit statistical model.

Forbidden wording includes “improved,” “worsened,” “better,” “bad trend,” “you are becoming biased,” “risk increased,” and recommendations inferred from direction. Issue and Strength labels retain their User-owned meaning; the system reports only their recorded frequency.

### Evidence navigation

Each window exposes its exact source reviews. Selecting a count or bucket opens the evidence list filtered to that window and label. Every evidence row links to its reviewed Expectation and shows the Journal Day, subject, retained Observation excerpt, Outcome, reasoning quality, and review explanation. Counts and denominators must be derivable from those records.

Edits and permitted deletions of mutable personal records recompute trends. The system does not imply that a previously displayed trend is an immutable historical fact.

## Examples

1. Current 7 days: 3 occurrences / 10 reviews. Previous 7 days: 1 / 10. Direction: higher observed share.
2. Current month through day 8: 2 / 8. Previous equal-length window: 1 / 4. Direction: same observed share.
3. Current: 2 / 4. Previous: 1 / 12. Direction: insufficient evidence because the current denominator is below 5.
4. Current: 0 / 9. Previous: 0 / 8. Direction: insufficient evidence because neither period contains an occurrence; show the two raw counts only if the label is otherwise present in Pattern Review.
5. Current: 1 / 9. Previous: 0 / 8. Direction: higher observed share. This describes retained records, not reasoning quality or improvement.

## Consequences

- Ticket 05 can implement one comparison model for weekly, monthly, and custom views.
- The threshold is deliberately simple and transparent; it is a display-support rule, not a claim of statistical significance.
- Partial current periods remain usable but visibly partial.
- Trend payloads must include both windows' dates, counts, denominators, support status, direction, and evidence links.
- No trend implementation begins until a maintainer changes this ADR to Accepted.
