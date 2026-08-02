# 01 — Watchlist list-first onboarding and complete add flow

Status: complete

## What to build

Replace the selector-first Watchlist screen with a list-first information hierarchy and one focused add flow. A first-time user should understand that the Watchlist holds Instruments that still need evidence, then choose an Instrument and provide a required Watchlist Note explaining why it remains relevant. Preserve existing membership ownership and duplicate handling. Extend the existing create path only as needed so membership and the required note are saved atomically; do not introduce new Watchlist domain types or statuses.

## Acceptance criteria

- [ ] The page title and subtitle explain that the Watchlist is for continuing observation, and an empty account shows one onboarding explanation with one primary add action.
- [ ] The add flow uses a normal searchable/keyboard-usable Instrument combobox and a required, clearly labelled Watchlist Note field; validation preserves entered values and prevents submission without both fields.
- [ ] Successful creation persists the Instrument membership and trimmed note in one valid user-owned operation, refreshes the list, and handles duplicate membership without creating a second record.
- [ ] Loading, unavailable directory, API error, cancel, and success states are distinct and accessible; no disabled control appears without an explanation.
- [ ] Existing remove, note-edit, and observation-timeline behavior continues to work, with tests covering empty, validation, success, duplicate, and 390px layouts.

## Blocked by

None - can start immediately.
