# 04 — Selected-day Journal Day detail and historical capture

Status: ready-for-human

Type: HITL

## What to build

Complete the selected-date workflow so Calendar answers what was recorded on a Journal Day and provides a safe path to continue that day. Before implementation, decide whether the existing observation history, expectation, Action Decision, and Discipline Principle queries can compose the required detail or whether a narrowly scoped read-only calendar detail contract is strictly necessary. The selected date must remain the source of truth for viewing and adding historical records.

## Acceptance criteria

- [ ] The data-source decision is documented: reuse existing query composition when it supplies the required records, or define the smallest read-only contract needed for selected-day details without changing unrelated backend behavior.
- [ ] Desktop shows the calendar beside a selected-day detail panel; the panel can close and return the calendar to full width.
- [ ] The panel shows the selected Journal Day’s full date, status, real observations, available Expectations, Action Decisions, Discipline Principle, and actions to add an observation or open the full Journal Day.
- [ ] A selected empty date shows an intentional empty state with actions for adding an observation and opening that Journal Day.
- [ ] Adding from a historical selection preserves the selected Journal Day context and cannot silently create a record for today.
- [ ] Tablet and mobile use a readable stacked, overlay, agenda, or bottom-sheet equivalent without horizontal scrolling; closing and reopening the detail preserves the selected date.
- [ ] The same observation and Journal Day presentation used by Today is reused where practical; no duplicate domain representation is introduced.
- [ ] Tests cover selection without reload, populated and empty details, historical add flow, date-context preservation, keyboard and announcement behavior, responsive layouts, and error/loading states.

## Blocked by

- [02-today-populated-journal-day-review.md](./02-today-populated-journal-day-review.md)
- [03-calendar-monthly-activity-overview.md](./03-calendar-monthly-activity-overview.md)
