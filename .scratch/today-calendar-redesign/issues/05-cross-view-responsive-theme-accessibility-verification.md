# 05 — Cross-view responsive, theme, and accessibility verification

Status: ready-for-agent

Type: AFK

## What to build

Verify and harden the completed Today and Calendar flows as one consistent micro-cockpit experience. Make the minimum shared UI, CSS, localization, and test changes needed for the four theme combinations, target viewport sizes, keyboard navigation, semantic structure, focus visibility, reduced motion, and real-data-only presentation. Capture the required Green Dark reference screenshots for handoff.

## Acceptance criteria

- [ ] Today and Calendar work at 1440px, 1024px, 768px, and 390px without clipped controls, accidental horizontal scrolling, or inaccessible action groups.
- [ ] Green Light, Green Dark, Red Light, and Red Dark preserve the semantic theme architecture; financial positive/negative colors remain independent from the accent family.
- [ ] Headings, labels, icon-only controls, form fields, calendar dates, selected-date announcements, and focus indicators meet the brief’s accessibility requirements and do not rely on color alone.
- [ ] English, Traditional Chinese, and Simplified Chinese render without missing-key warnings or malformed copy in the redesigned flows; code comments remain in English.
- [ ] Existing Today and Calendar behavior, query/mutation contracts, navigation, and valid tests remain intact.
- [ ] Green Dark screenshots are produced for Today and Calendar at 1440px and 390px and attached or linked from the issue’s verification notes.
- [ ] The final verification notes list implementation summary, changed files, shared components, reused APIs, backend limitations, test commands/results, and deliberately deferred items.

## Blocked by

- [01-today-quick-capture-workspace.md](./01-today-quick-capture-workspace.md)
- [02-today-populated-journal-day-review.md](./02-today-populated-journal-day-review.md)
- [03-calendar-monthly-activity-overview.md](./03-calendar-monthly-activity-overview.md)
- [04-selected-day-journal-day-detail.md](./04-selected-day-journal-day-detail.md)
