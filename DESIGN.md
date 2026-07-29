# Micro Cockpit design

## 1. Product character

Micro Cockpit is a calm, precise, restrained instrument for preserving a market view and reviewing how that view develops. Observation comes before transaction. A day without a Trade remains a complete day.

No confetti, streaks, urgency theatre, streaming-price density, generic education catalogue, or system-authored judgement.

## 2. Visual system

- Use the existing semantic tokens, Inter for interface text, and Newsreader only for reflective prose.
- Maintain WCAG 2.2 AA contrast and keyboard access.
- Prefer 44px touch targets; never go below 24px.
- Do not encode state or direction by color alone.
- Honor reduced motion and keep transitions incidental.
- Keep money labeled with currency and numerics tabular.

## 3. Navigation and routes

Authenticated navigation:

```text
Today · Review · Watchlist · Calendar · Tools · Settings
```

| Route | Purpose |
|---|---|
| `/today` | Quick Observation, today’s updates, Expectations, decisions, and selected principle |
| `/today/observations` | Filtered Observation history; it is not a separate research product |
| `/review` | Expectation Review, Pattern Review, and Discipline Principles |
| `/watchlist` | Instruments deliberately kept under observation, with short notes |
| `/calendar/:year/:month` | Journal Day activity and review readiness |
| `/tools` | Public calculators plus authenticated presets and Calculation Snapshots |
| `/settings` | Preferences, Agent Users, Access Grants, export, deletion |

Removed routes render the ordinary not-found state. There are no compatibility screens for Diary, reminders, Performance, Partner Compare, price alerts, rotation, articles, or a standalone research timeline.

## 4. Core interaction patterns

### Today

Quick Observation is the primary action. It asks “What did you notice today?” and saves a timestamped update to the current Journal Day. Structure is optional and can be added later:

- Signal: the retained fact or condition.
- Interpretation: what the User believes it means.
- Subject: market, sector, theme, or Instrument.
- Expectation: testable behavior, horizon, invalidation condition, and confidence.
- Action Decision and Trade: optional evidence of intended and actual action.

Editing historical content shows an honesty reminder but remains allowed.

### Review

Review is owner-authored. Outcome and reasoning quality are separate fields. Pattern Review shows counts, denominators, and links to evidence. A recurring label does not become a Confirmed Pattern until the User accepts it and chooses a Discipline Principle.

The Human / Agent comparison selects one subject or Instrument and a date range. It shows separately owned Observation Updates and Expectations in labeled columns, constrained by the existing Access Grant. Only objective latest-Outcome consistency and confidence difference are summarized; missing data remains unavailable and neither side can be edited here.

### Watchlist

Membership means “continue observing,” not “own,” “buy,” or “hold.” A Watchlist Note is short context, not a research timeline.

### Calendar

Each day shows Observation Update count and review-ready count. Empty dates remain neutral. Calendar never fabricates performance.

### Tools

Position Size, Risk/Reward, Average Cost, and Profit/Loss are deterministic public calculators. Authenticated Users may save presets and Calculation Snapshots. There are no Trade Draft or Diary Draft actions. Average Cost and Profit/Loss remain standalone and never claim portfolio truth.

## 5. Responsive behavior

Mobile is the reference layout. Forms stack in reading order; comparison content stacks with explicit owner headings. Desktop may use two columns where reading order remains clear. Primary navigation is always available in both sidebar and mobile bar.

## 6. Internationalization

English and Traditional Chinese use the typed message catalogue. New interface copy must be added to both locales and accessed through `MessageKey`; do not add raw strings to core workflows.

## 7. Privacy and ownership

Human and Agent User records display their named owner. Access Grants are read-only and revocable. Shared records never become editable through a comparison or review surface. Missing or inaccessible material is shown as unavailable without revealing whether an undisclosed record exists.

## 8. System Never Judges

The system may present objective relationships, ordering, frequency, and retained evidence. It may not decide an Outcome, diagnose reasoning, or merge two owners’ views into a system conclusion.

Allowed:

> Your invalidation condition was “Daily Close below 95”; the Daily Close was 93.

Not allowed:

> Your thesis failed.

Judgement must belong to a named human or Agent User. Outcome prompts use factual phrasing and leave the decision to that owner.

## 9. Content voice

- Plain, adult, and direct.
- “Unavailable” instead of invented zeroes.
- “No observation recorded” instead of pressure to participate.
- “Review the retained evidence” instead of “See what you got wrong.”
- Never congratulate, scold, diagnose, or recommend a Trade.

## 10. Page-state checklist

Every core page must provide:

- loading, empty, failure, and retry states;
- visible labels and validation;
- keyboard-operable actions and focus behavior;
- non-color status labels;
- English and Traditional Chinese copy;
- a usable narrow-phone layout.
