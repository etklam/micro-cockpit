# 02 — Watchlist items show why and what to do next

Status: complete

## What to build

Make each populated Watchlist item useful for the next observation decision. Render only fields available from the current model and market directory, with the Watchlist Note as the reason for tracking. Add client-side search and recent-activity sorting over real values, and make the existing timeline, add-observation, edit-note, and remove actions discoverable without exposing backend terminology or inventing statuses, counts, prices, or review dates.

## Acceptance criteria

- [ ] Each item shows its real symbol/name, user-facing Instrument type, Watchlist Note (or a clear missing-note state), and available created/updated time.
- [ ] Search filters by real symbol, name, or note; sorting by recent activity is deterministic and has an accessible empty filtered state.
- [ ] The primary next action is visually and semantically clear, and existing observation timeline navigation preserves the Instrument context without duplicating records.
- [ ] Note editing keeps the existing 500-character validation and preserves input on failure; removal requires an accessible confirmation before mutation.
- [ ] Tests cover populated items, filtering, sorting, note edit/error, removal confirmation, keyboard actions, and responsive card layout.

## Blocked by

- 01-watchlist-list-first-onboarding.md
