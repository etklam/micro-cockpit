# 02 — Today populated Journal Day review

Status: ready-for-agent

Type: AFK

## What to build

Turn the populated Today page into a dense Journal Day review workspace. Present today’s Market Observations as structured, reusable cards with their real subjects, tags, expectation and Action Decision relationships, and existing actions. Add a desktop secondary column for real summary counts and today’s Discipline Principle, then show up to three real recent observations with navigation to their Journal Day or detail destination. Extract or reuse the presentation pieces needed by Calendar so the same domain data is not represented twice.

## Acceptance criteria

- [ ] The Today page has a clear hierarchy: current Journal Day context, composer, today’s observations, summary/discipline, and recent observations.
- [ ] The populated observation section shows the real count and renders time, title/preview, available subjects/tags, relationships, and only actions that already have behavior.
- [ ] Empty, loading, and error states remain explicit and do not use fabricated records, counts, dates, or summaries.
- [ ] Desktop uses a main observation column plus a compact secondary column; tablet and mobile stack the secondary content below the observations without horizontal scrolling.
- [ ] The summary shows only counts derived from real data, and the Discipline Principle card supports both populated and empty states with the existing management destination.
- [ ] Up to three real recent observations are shown with one-, two-, and three-item layouts; mobile uses one column; each item navigates to a real detail or Journal Day destination.
- [ ] Shared observation, Journal Day summary, empty-state, Discipline Principle, and recent-observation presentation is extracted or reused where practical, with data access remaining in the existing query/feature layer.
- [ ] Tests cover populated and empty observations, recent-observation navigation, discipline states, retained observation/expectation/decision actions, responsive behavior, keyboard access, and all supported locales.

## Blocked by

- [01-today-quick-capture-workspace.md](./01-today-quick-capture-workspace.md)
