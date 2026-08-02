# 03 — Calendar monthly activity overview

Status: ready-for-agent

Type: AFK

## What to build

Redesign Calendar as Journal Day navigation and historical activity overview. Preserve the existing month route and calendar query while adding an explicit Today action, accessible previous/next month controls, a compact month summary when real values are available, and dense day cells that communicate activity without meaningless punctuation or a single color carrying multiple meanings.

## Acceptance criteria

- [ ] The page presents its title, short explanation, Today action, previous month, current month/year, next month, view control when supported, and add-observation action with accessible names.
- [ ] Month navigation updates the route and query state without a full-page reload; Today navigates to and selects the account’s current Journal Day.
- [ ] The monthly summary uses only real calendar data. Records and active days are shown when available; expectation, decision, or review metrics are omitted when the current contract does not provide them rather than filled with placeholders.
- [ ] Each day cell can show its day number, today indicator, selection state, real activity/category indicators, up to two real previews or counts, and an additional-content marker only when additional content exists.
- [ ] Today, selected, active, needs-review, and empty states are visually distinct and are not communicated by color alone.
- [ ] Selecting a day updates the URL-preserved selection without reloading the page, keeps the selection announced accessibly, and supports keyboard movement between dates.
- [ ] The calendar uses compact cells at mobile widths without horizontal scrolling; tablet and desktop layouts preserve readable controls and spacing.
- [ ] Tests cover current-month rendering, previous/next navigation, Today, date selection, activity counts, keyboard behavior, empty states, supported themes, and supported locales.

## Blocked by

None - can start immediately.
