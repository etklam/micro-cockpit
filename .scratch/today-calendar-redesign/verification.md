# Verification notes

## Implementation summary

- Today now uses a compact, expandable quick-capture composer, preserves drafts while the user works, and separates the real current-day observation/expectation summary from recent history.
- Calendar now provides month navigation, URL-preserved date selection, activity-aware day cells, a selected Journal Day detail panel, keyboard date movement, empty/loading/error states, and a historical observation composer.
- Watchlist now has a first-target onboarding flow that searches the published instrument directory, supports keyboard selection, accepts a reason, announces success, and focuses the created item.
- Review now defaults to the pending self-review queue, keeps pending and completed work separate, guides the Human self-review flow, and keeps Agent comparison secondary and collapsed.

## Data-source decisions

- Calendar reuses the existing calendar, observation-history, expectations, Action Decision, and Today discipline queries. No separate read-only detail endpoint was introduced.
- Historical quick capture uses the existing quick-observation endpoint with the smallest additive `journalDay` request field. The backend uses the supplied Journal Day and the frontend invalidates the relevant history and calendar queries after save.
- Watchlist creation remains limited to published market instruments because the existing contract has no Subject/Event/Custom target creation or external search API. The UI communicates that availability instead of inventing unsupported records.
- Review self-review uses the existing expectation-review write contract. Historical immutable snapshot/evidence fields are not present in the current API and remain explicitly disclosed in the UI.

## Verification

- `npm test`: 16 files, 81 tests passed.
- `npm run build`: passed.
- `npm run lint`: passed with the repository’s existing Fast Refresh warnings only.
- `dotnet build TradeDiary.slnx --nologo -m:1 --disable-build-servers`: passed; existing `Microsoft.OpenApi` vulnerability warnings remain.
- `dotnet test TradeDiary.slnx --no-build --nologo -m:1`: Edge API tests passed (51); the remaining service/database suites are blocked by Docker/Testcontainers being unavailable in the environment.
- Calendar historical capture is covered by an MSW contract test asserting the selected `journalDay` reaches the write request.
- Browser smoke testing reached the local authenticated shell’s sign-in screen, but no test session was available. No credentials or fake production data were used, so authenticated desktop/mobile screenshots remain a manual handoff item.
