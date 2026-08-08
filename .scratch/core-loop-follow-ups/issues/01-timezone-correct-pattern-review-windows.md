# Make Pattern Review windows respect Journal Day timezone

Status: complete

Type: AFK

## What to build

Make weekly, monthly, and custom Pattern Review windows use the authenticated User's authoritative Journal Day timezone and rollover. Counts, denominators, first/most-seen dates, and returned evidence must all be selected against the same local-day boundaries used by Today and Calendar, while the service continues to query stored UTC timestamps safely.

## Acceptance criteria

- [x] Weekly and monthly windows end on the User's current Journal Day, including non-midnight rollover settings.
- [x] Custom `from` and `to` values represent inclusive Journal Days and are converted to correct UTC boundaries before querying reviews.
- [x] DST gap and fold behavior matches the repository's authoritative Journal Day behavior.
- [x] Invalid, overflowing, or unsupported ranges return a controlled `400` response instead of throwing or issuing an unbounded query.
- [x] One User's timezone or rollover settings can never affect another User's aggregation.
- [x] UI date labels, API range metadata, counts, denominators, and evidence agree at boundary cases.
- [x] Journal integration tests cover UTC, Asia/Taipei, a non-midnight rollover, DST transition days, and cross-user isolation.
- [x] Runtime OpenAPI, composed Edge OpenAPI, and the generated frontend client have no drift.

## Blocked by

None - can start immediately.

## Comments

Implemented without an API shape change, so shared OpenAPI documents and the generated client require no regeneration for this ticket.
