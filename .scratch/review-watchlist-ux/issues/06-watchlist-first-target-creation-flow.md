# 06 — Watchlist first-target creation flow

Status: ready-for-human

Type: HITL

## Parent

- [01 — Watchlist list-first onboarding and complete add flow](./01-watchlist-list-first-onboarding.md)

## What to build

Fix the first-time-user dead end in Watchlist by making “新增觀察標的” one coherent workflow. A user must be able to search or select a supported existing local target, create a supported target when the backend allows it, provide the reason it remains worth observing, and create the Watchlist membership without leaving the Watchlist page or submitting a second time. Keep backend entities separate and do not present unsupported target types or fabricated search results. Before implementation, document the smallest supported target/data contract because the current Watchlist API accepts an `instrumentId`, while the existing frontend does not expose Subject/Instrument creation or external symbol search.

## Acceptance criteria

- [ ] The supported target types and data source are documented before implementation; the UI presents only types supported by the resulting backend contract and does not fake topic, event, custom, or external-search capability.
- [ ] The empty Watchlist state uses user-facing “觀察標的” terminology, one focused onboarding card, and a real “新增第一個觀察標的” action; it does not show a disabled selector or the dead-end “沒有可加入的金融工具” message.
- [ ] The add flow opens in an accessible modal or drawer with a keyboard-usable target search/selection step, clear distinction between existing local records and supported external results, and a supported custom-target path only when the backend can persist it.
- [ ] The flow requires a target and “為何值得持續觀察？”, preserves entered values through validation and recoverable errors, and renders optional context fields only when real persistence exists.
- [ ] Selecting an equivalent existing target skips target creation and creates only the Watchlist membership; duplicate active memberships are rejected or handled without creating a second record.
- [ ] A successful submission reuses or creates the target, creates the Watchlist item, refreshes or updates the Watchlist query cache, closes the flow, announces success, and displays or focuses the new item without a second add action.
- [ ] If target creation succeeds but Watchlist creation fails, the user receives a clear retry path with form values preserved and no confusing duplicate target creation.
- [ ] Any existing management route is secondary and not required for normal Watchlist creation; no large administration module is added when no such route exists.
- [ ] Tests cover the first-time empty state, existing local target, supported external result, supported custom target, required-reason validation, duplicate handling, search-empty/loading states, partial-failure retry, cache refresh, keyboard operation, and mobile modal/drawer layout.
- [ ] Verification notes report the previous and implemented data flows, APIs reused or added, duplicate-prevention behavior, test results, and any deferred external-symbol-search integration.

## Blocked by

None - can start immediately.
