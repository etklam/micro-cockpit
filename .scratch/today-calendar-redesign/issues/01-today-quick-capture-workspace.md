# 01 — Today quick-capture workspace

Status: ready-for-agent

Type: AFK

## What to build

Make Today the focused entry point for the current Journal Day. Preserve the existing Quick Observation association, mutation, validation, loading, success, and error behavior while replacing the oversized composer treatment with a compact, expandable capture workspace. Correct the greeting/context hierarchy and provide a compact, actionable empty state when today has no observations.

## Acceptance criteria

- [ ] The header presents a natural greeting or neutral daily heading, the account Journal Day date, and only functional or explained actions.
- [ ] The composer uses the existing Quick Observation write path, keeps draft text through unrelated rerenders, expands with content, preserves visible focus, and keeps the primary save action close to the input.
- [ ] The save action is disabled while the input is invalid or the mutation is pending, and existing success/error behavior remains intact.
- [ ] Character count and draft/autosave messaging are shown only when supported by real product behavior or a real limit.
- [ ] An empty current Journal Day shows a compact onboarding card with actions that lead to the real observation flow and observation list where supported; it is not a large vertically centered empty region.
- [ ] The feature is covered by tests for the create flow, empty state, pending/disabled state, draft preservation, focusable controls, and supported mobile layout.
- [ ] Existing queries, mutations, validation, navigation, theme tokens, and English/Traditional Chinese/Simplified Chinese message architecture remain intact.

## Blocked by

None - can start immediately.
