# Discipline Service

Owns only `discipline.disciplines`.

- Today selection is a stable SHA-256 choice for user + local date.
- Random browsing is stateless and never changes Today selection.
- Reorder accepts exactly the authenticated user's full ID set and commits atomically.
- No streaks, checks, or gamification state.
