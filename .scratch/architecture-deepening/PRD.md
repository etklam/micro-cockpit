# Deepen the Market Reflection Architecture

Status: ready-for-agent

## Problem Statement

Micro Cockpit's Market Observation product is functionally complete, but several load-bearing domain rules are not concentrated behind deep modules.

The most visible problem is Journal Day. A Journal Day is defined by a User's timezone and chosen rollover, yet Bootstrap currently derives its displayed date from timezone alone. Before a non-midnight rollover, Today and Calendar can therefore present a date that differs from the Journal Day used when reading or creating a Market Observation.

Security-sensitive Access Grant behavior is repeated across full synchronization, incremental synchronization, and Human / Agent comparison. Fixed and ongoing closure, date and subject selection, expiry, revocation, deletion tombstones, and owner isolation are encoded in several query paths. This reduces locality and makes it expensive to prove that every read mode applies the same grant semantics.

Agent User provisioning, manager attribution, API Token rotation, exchange, revocation, usage tracking, export, and account deletion are implemented as route-local transaction sequences. The lifecycle is governed by ADR-0004 and ADR-0007, but the implementation does not yet provide one testable domain interface that preserves those decisions.

The frontend has broad pass-through modules between pages and the generated client. Market Observation, Expectation Review, Agent User, and Access Grant behavior is spread across transport aliases, query keys, mutation invalidation rules, payload mapping, and large page implementations. Callers must learn too much of the generated client and cache implementation.

Instrument continuity and completed-session Daily Close publication are similarly interleaved with route and persistence details. Stable Instrument identity, symbol history, provider-run state, staged rows, successful publication, failed-run invisibility, and Daily Close reads are currently verified mainly through one large integration path.

These problems make future changes harder to reason about and harder to test in isolation. They also increase the risk that two callers interpret the same domain concept differently.

## Solution

Deepen five areas of the existing architecture without changing the product model or process ownership:

1. Establish one authoritative Journal Day module used by Bootstrap, Today, Calendar, and Market Observation writes. Its behavior includes timezone, rollover, DST gaps, DST folds, and invalid preference handling.
2. Concentrate Access Grant eligibility and closure behind one Journal-owned seam. Full synchronization, incremental synchronization, and Human / Agent comparison remain distinct adapters over the same security rules.
3. Concentrate the complete Agent User lifecycle behind one Identity-owned seam. Human-provisioned and future platform-operated Agent Users share the same ownership, manager-attribution, and credential invariants.
4. Replace broad frontend pass-throughs with deep domain modules for Market Observation, Expectation / Review, and Agent User / Access Grant flows. The generated client remains the transport adapter.
5. Concentrate stable Instrument identity and completed-session Daily Close publication into separate Market Data-owned modules with explicit state transitions.

The refactor preserves the existing public product behavior except for correcting Bootstrap and Calendar to use the actual Journal Day. It does not change record ownership, Access Grant authority, personal-record mutability, immutable-event privacy, process ownership, or the System Never Judges commitment.

## User Stories

1. As a Market Participant, I want Today to display my actual Journal Day, so that the date matches where my Quick Observation will be stored.
2. As a Market Participant, I want Calendar to open on my actual Journal Day, so that overnight observations appear under the expected date.
3. As a Market Participant, I want a non-midnight rollover respected everywhere, so that one market session is not presented as two different days.
4. As a Market Participant, I want Bootstrap and Journal to interpret timezone and rollover identically, so that navigation and stored records cannot disagree.
5. As a Market Participant, I want Journal Day behavior to remain correct during daylight-saving transitions, so that seasonal clock changes do not misdate observations.
6. As a Market Participant, I want a nonexistent rollover time during a DST gap handled consistently, so that the product does not fail unpredictably.
7. As a Market Participant, I want an ambiguous rollover time during a DST fold resolved consistently, so that the same instant always maps to the same Journal Day.
8. As a Market Participant, I want local midnight to remain the default rollover, so that existing accounts retain expected behavior.
9. As a Market Participant, I want changing my timezone or rollover to affect subsequent Journal Day resolution consistently, so that all active product surfaces use my current preference.
10. As a Market Participant, I want historical Market Observations to retain their stored Journal Day, timezone, and rollover context, so that a later settings change does not rewrite history.
11. As a Market Participant, I want the Today heading date to match the Market Observation returned by Today, so that the page does not contradict its content.
12. As a Market Participant, I want a newly saved Quick Observation to report the same Journal Day that Bootstrap displays, so that save feedback is trustworthy.
13. As a Market Participant, I want Calendar's default selected date to match Today, so that moving between the two surfaces preserves context.
14. As a User, I want corrupt timezone or rollover preferences to produce a controlled error, so that the product does not silently select an unrelated UTC date.
15. As a developer, I want Journal Day behavior behind one stable interface, so that a rule change is implemented once.
16. As a developer, I want Bootstrap and Journal Day tests to share the same behavior matrix, so that cross-adapter disagreement is caught before release.
17. As a human User, I want an Agent User to read only records covered by an active Access Grant, so that my private records remain private by default.
18. As a human User, I want expired Access Grants rejected in every read mode, so that expiry cannot be bypassed by choosing another endpoint.
19. As a human User, I want revoked Access Grants rejected in every read mode, so that revocation has one clear security effect.
20. As a human User, I want Access Grant date selection interpreted consistently, so that full sync, incremental sync, and comparison expose the same eligible days.
21. As a human User, I want subject-scoped Access Grants interpreted consistently, so that an Agent User cannot widen access through a different query.
22. As a human User, I want Instrument-scoped Access Grants interpreted consistently, so that related subject matching cannot leak unrelated Instruments.
23. As a human User, I want owner isolation applied before content is materialized, so that filtering cannot reveal another User's records.
24. As a human User, I want a fixed Access Grant to retain its captured closure, so that later-created child records remain excluded.
25. As a human User, I want edits to already included fixed records to remain visible while the grant is active, so that fixed scope does not imply stale content.
26. As a human User, I want an ongoing Access Grant to include later matching records, so that continuing analysis does not require repeated grant creation.
27. As a human User, I want an ongoing Access Grant to stop including a record that no longer matches retained content where the existing rules require it, so that current scope remains meaningful.
28. As an Agent User, I want full synchronization to return complete eligible record closures, so that child records are not detached from their Market Observation.
29. As an Agent User, I want incremental synchronization to apply the same grant selection as full synchronization, so that a cursor cannot widen access.
30. As an Agent User, I want incremental changes ordered deterministically, so that I can advance a cursor without missing eligible changes.
31. As an Agent User, I want an expired cursor to return the existing fresh-sync requirement, so that recovery remains explicit.
32. As an Agent User, I want deletion changes to contain only record identity, type, operation, and time, so that deleted personal content is not retained.
33. As an Agent User, I want Human / Agent comparison available only when the requested subject and date range are covered, so that comparison does not bypass grant intent.
34. As a human User, I want Human and Agent-owned records kept in separate owner-labelled collections, so that the product never merges their views.
35. As a human User, I want comparison to remain read-only, so that observing an Agent User's view cannot mutate either owner's records.
36. As a human User, I want comparison to report unavailable and empty states distinctly, so that absent permission is not confused with no matching observations.
37. As a developer, I want one Access Grant eligibility interface used by three concrete adapters, so that security rules have locality.
38. As a developer, I want fixed closure, ongoing closure, and tombstone behavior independently testable, so that one failure does not require debugging a complete synchronization scenario.
39. As a security reviewer, I want an eligibility matrix covering owner, time, subject, Instrument, mode, revocation, and expiry, so that grant behavior can be audited systematically.
40. As a human User, I want to provision an Agent User through one coherent lifecycle, so that identity and credential creation cannot partially disagree.
41. As a human User, I want each Agent User to have at most one active API Token, so that credential management remains simple.
42. As a human User, I want rotating an Agent Token to revoke the previous Token atomically, so that there is no unintended overlap.
43. As a human User, I want revoking an Agent Token to prevent future exchanges immediately, so that stopping access is predictable.
44. As a human User, I want another User unable to rotate or revoke my Agent User's Token, so that manager ownership is enforced centrally.
45. As an Agent User, I want a valid API Token exchanged for an access token with only its granted scopes, so that credential authority is not widened.
46. As an Agent User, I want invalid, revoked, or inactive credentials rejected consistently, so that credential state has one interpretation.
47. As a human User, I want last-used and last-successful-request timestamps updated consistently, so that Agent activity is understandable.
48. As a human User, I want account export to include managed Agent User metadata without credential secrets, so that portability does not leak Tokens or hashes.
49. As a human User, I want account deletion to remove managed Agent User credentials and identity records in the established order, so that leaving the product removes delegated access.
50. As a platform operator, I want platform-operated Agent Users to remain representable, so that future named AI judgement does not require a new ownership model.
51. As a Market Participant, I want every Agent-authored judgement attributed to its Agent User, so that the System Never Judges commitment remains intact.
52. As a developer, I want provisioning, rotation, exchange, revocation, usage, export, and deletion behind one Agent User lifecycle interface, so that shared invariants have locality.
53. As a developer, I want route adapters to translate transport concerns without owning lifecycle transactions, so that lifecycle tests do not require every route.
54. As a developer, I want focused lifecycle tests for human and platform manager kinds, so that ADR-0007 remains executable.
55. As a frontend developer, I want Market Observation transport adaptation, cache keys, and mutation consequences in one module, so that pages do not learn generated operation details.
56. As a frontend developer, I want Expectation and Expectation Review behavior in one module, so that Today and Review use the same cache and state transitions.
57. As a frontend developer, I want Agent User and Access Grant management behavior in one module, so that Settings does not own transport and cache rules.
58. As a frontend developer, I want the generated client to remain an adapter, so that regenerated operation names do not become the product's domain interface.
59. As a frontend developer, I want shallow one-line aliases removed where they add no behavior, so that navigation through the codebase is shorter.
60. As a frontend developer, I want cache invalidation colocated with the mutation that causes it, so that stale Today, Review, or Calendar data is less likely.
61. As a frontend developer, I want query enablement and not-found normalization owned by the relevant domain module, so that pages receive meaningful states.
62. As a frontend developer, I want payload construction and domain validation separated from large render implementations, so that they can be tested without DOM noise.
63. As a User, I want saving an Expectation to refresh Today, Review, and Calendar consistently, so that all affected views reflect the change.
64. As a User, I want saving an Expectation Review to refresh readiness and Pattern Review consistently, so that reviewed state is not stale.
65. As a User, I want changing an Access Grant to refresh management state without affecting unrelated cached records, so that the interface remains stable.
66. As a frontend tester, I want domain module tests to assert observable states and cache consequences, so that DOM tests can focus on accessibility and interaction.
67. As a frontend tester, I want existing screen-level golden paths retained, so that deeper modules do not weaken end-user coverage.
68. As a developer, I want new frontend seams only where multiple callers or concrete adapters exist, so that the refactor does not create hypothetical abstractions.
69. As a Market Participant, I want an Instrument to retain its identity when its symbol changes, so that historical Observation Subjects remain connected.
70. As a Market Participant, I want only one current active symbol exposed for an Instrument, so that current display and lookup are unambiguous.
71. As a Market Participant, I want inactive aliases excluded from the current Instrument directory, so that retired symbols are not presented as current.
72. As a data operator, I want assigning a symbol to another Instrument rejected atomically, so that Instrument continuity cannot be corrupted.
73. As a data operator, I want symbol-history transitions committed with current-symbol changes, so that the two representations cannot disagree.
74. As a data operator, I want a provider run to accept valid staged Daily Close rows only while it is running, so that completed runs cannot be mutated.
75. As a data operator, I want invalid bars rejected before publication, so that malformed completed-session evidence remains invisible.
76. As a data operator, I want duplicate rows within a provider run handled deterministically, so that safe retries do not create conflicting evidence.
77. As a data operator, I want a successful provider run to publish its staged rows atomically, so that Users never see a partially published session.
78. As a data operator, I want a failed provider run to leave its staged rows unpublished, so that failed ingestion cannot become Daily Close evidence.
79. As a data operator, I want provider-run state transitions enforced centrally, so that each route cannot invent a different lifecycle.
80. As a Market Participant, I want Daily Close to remain unavailable until publication succeeds, so that incomplete data is not presented as evidence.
81. As a Market Participant, I want raw and adjusted close retained distinctly, so that exact levels and corporate-action-aware review remain possible.
82. As a Market Participant, I want unsupported or missing Daily Close represented explicitly, so that absence is not fabricated as zero.
83. As a Journal maintainer, I want the Daily Close consumer adapter to depend only on versioned published reads, so that private Market Data persistence remains hidden.
84. As a Market Data maintainer, I want Instrument continuity and publication state tested through their domain interfaces, so that route tests remain small.
85. As an operator, I want existing health, deployment, migration, and release-smoke checks to remain green, so that architecture improvement does not reduce operability.
86. As a maintainer, I want every deepened module to increase locality without changing process ownership, so that the refactor remains incremental.
87. As a maintainer, I want every retained seam justified by multiple callers or concrete adapters, so that abstraction cost remains proportional to leverage.
88. As a maintainer, I want deleted pass-through modules to reduce interface surface, so that AI and human navigation require fewer hops.
89. As a maintainer, I want existing public contracts preserved unless this PRD explicitly corrects Journal Day naming or semantics, so that the refactor does not create unrelated migration work.
90. As a maintainer, I want each implementation slice independently releasable and testable, so that the architecture can deepen without a flag-day rewrite.

## Implementation Decisions

- Preserve the active process ownership: Identity owns Users, Agent Users, managers, and credentials; Journal owns Journal Days, Market Observations, Access Grants, synchronization, and comparison; Market Data owns Instruments, symbol history, provider runs, and Daily Close; Edge owns browser-facing composition; Tool is unchanged.
- Preserve ADR-0001 and ADR-0002: no cross-schema reads, writes, foreign keys, or transactions are introduced.
- Preserve ADR-0004: Access Grants remain Journal-owned because their scope uses Journal concepts.
- Preserve ADR-0005: personal reflection records remain mutable and are not turned into an immutable audit log.
- Preserve ADR-0006: immutable record-change and deletion data remains content-free.
- Preserve ADR-0007 and the System Never Judges principle: Agent User judgement remains separately owned and labelled.
- Build one deep Journal Day module with a small stable interface accepting an instant, timezone, and rollover and returning the authoritative Journal Day or a controlled validation failure.
- Use the Journal Day module from both Journal and Edge adapters rather than maintaining a timezone-only Edge interpretation.
- Rename the Bootstrap concept from `currentLocalDate` to `currentJournalDay` across its contract, generated client, and frontend callers. The product is not yet launched, so no compatibility alias is required.
- Keep Identity as the owner of timezone and rollover preferences. The Journal Day module interprets those values but does not persist them.
- Do not recalculate historical Market Observation dates after settings changes. Stored Journal Day, timezone, and rollover remain record context.
- Preserve the existing DST policy: a rollover inside a gap advances to the first valid local minute; a rollover inside a fold uses the earlier occurrence.
- Treat invalid persisted timezone or rollover values as a controlled failure. Do not silently substitute UTC for the authoritative Journal Day.
- Keep Calendar month composition in Edge, but derive its default month and selected day from `currentJournalDay`.
- Build one deep Access Grant selection module that owns active-grant checks, owner relationships, date selection, subject and Instrument selection, and fixed-versus-ongoing mode semantics.
- Build one deep Access Grant closure module that owns which Market Observation child records are materialized for an eligible grant.
- Keep full synchronization, incremental synchronization, and Human / Agent comparison as three concrete adapters over the shared Access Grant seams.
- Keep filter parsing, cursor parsing, and transport error translation at adapter level unless the behavior is shared by multiple adapters.
- Preserve the current full-sync and incremental cursor formats and expiration behavior.
- Preserve the difference between current-content reads and content-free deleted changes.
- Preserve fixed-grant closure capture at grant creation. Later-created child records remain excluded, while current retained content for captured IDs remains readable.
- Preserve continuous evaluation for ongoing grants.
- Human / Agent comparison continues returning owner-separated records and objective differences only. It does not create, edit, merge, or judge records.
- Build one deep Agent User lifecycle module inside Identity. It owns provisioning, manager attribution, listing, Token creation, rotation, revocation, exchange validation, usage tracking, export projection, and deletion participation.
- Keep HTTP and service-key handlers as adapters over the Agent User lifecycle module.
- Keep Agent User lifecycle transactions inside the Identity-owned schema.
- Preserve exactly one active API Token per Agent User and atomic revoke-before-replace behavior.
- Preserve Token hash storage and one-time raw Token disclosure. Exports never include raw Tokens or hashes.
- Preserve the distinction between human-managed and platform-operated Agent Users.
- Do not move Access Grant checks into Identity or teach Identity about Observation Subject, Instrument, or Journal Day scope.
- Replace the broad frontend transport alias and query collections with deep domain modules for:
  - Market Observation and Observation Update;
  - Expectation and Expectation Review;
  - Agent User and Access Grant.
- Each frontend domain module owns generated-client adaptation, query keys, not-found normalization, query enablement, and mutation-driven cache consequences for its domain flow.
- Page modules retain rendering, accessibility, route state, and ephemeral interaction state. They do not own generated operation names or cross-page cache knowledge.
- Keep the generated client as the concrete transport adapter and continue regenerating it from Edge OpenAPI.
- Do not introduce a new state-management library, form library, or runtime dependency.
- Delete pass-through aliases only when they fail the deletion test. Keep modules that hide meaningful error normalization, idempotency, cache, or domain mapping behavior.
- Build one deep Instrument identity module inside Market Data. It owns symbol normalization, Instrument creation and lookup, active-symbol replacement, inactive aliases, symbol conflicts, and symbol-history transitions.
- Build one deep Daily Close publication module inside Market Data. It owns provider-run creation, running-state validation, staged bar validation, idempotent row replacement within a run, completion, success publication, and failed-run invisibility.
- Keep HTTP handlers as adapters over the Instrument identity and Daily Close publication modules.
- Keep Journal's Daily Close enrichment as a consumer adapter over versioned published Market Data reads.
- Preserve existing public read contracts for Instrument directory, Instrument lookup, bars, Daily Close, and provider health.
- No historical migration file is modified. Add a forward-only migration only if an implementation invariant cannot be enforced using the existing schema.
- Deliver the work as independently verifiable vertical slices in this order:
  1. Journal Day correctness and Bootstrap contract rename;
  2. Access Grant selection and closure;
  3. Agent User lifecycle;
  4. frontend domain modules;
  5. Instrument identity and Daily Close publication.
- Each slice must leave the full build and existing release path green. No flag-day rewrite is required.

## Testing Decisions

- Good tests assert externally observable behavior through a module interface or concrete adapter. They do not assert private helper names, file organization, SQL text, React hook count, or internal class shapes.
- Every one of the five deepened areas receives focused tests.
- Journal Day tests use the existing table-driven prior art for timezone, rollover, DST gaps, and DST folds.
  - Run the same behavior matrix through the authoritative Journal Day interface.
  - Add Bootstrap coverage before and after a non-midnight rollover.
  - Assert that Bootstrap `currentJournalDay`, Today reads, Quick Observation writes, and Calendar defaults agree.
  - Assert controlled failure for invalid persisted timezone or rollover.
  - Assert that historical stored Journal Days do not change after settings changes.
- Access Grant tests use the existing real-PostgreSQL Journal integration seam.
  - Replace reliance on two giant scenarios with a focused matrix covering owner, Agent User, date, subject, Instrument, fixed mode, ongoing mode, expiry, revocation, and record deletion.
  - Exercise the same eligibility cases through full synchronization, incremental synchronization, and Human / Agent comparison adapters.
  - Prove that no filter widens the grant.
  - Prove that fixed grants exclude later-created children and include edits to captured records.
  - Prove that ongoing grants include later matching records.
  - Prove that deleted changes contain no personal content.
  - Retain a small end-to-end adapter test for each read mode.
- Agent User lifecycle tests use the existing Identity application-hosting and PostgreSQL-container prior art.
  - Test provisioning as a human manager.
  - Test representation of a platform-operated Agent User.
  - Test manager isolation.
  - Test one-active-Token behavior under creation and rotation.
  - Test revoked, malformed, expired where applicable, and inactive-account exchange failures.
  - Test usage timestamps.
  - Test export redaction.
  - Test account deletion cleanup.
  - Retain HTTP adapter tests for status and contract translation.
- Frontend domain module tests use the existing Vitest, Testing Library, MSW, and TanStack Query prior art.
  - Test domain query states, not-found normalization, enablement, and mutation consequences at each module interface.
  - Test Market Observation mutations refresh Today and affected Calendar data.
  - Test Expectation and Expectation Review mutations refresh Today, Review, Calendar, and Pattern Review where applicable.
  - Test Agent User and Access Grant mutations refresh only their relevant management data.
  - Retain screen tests for rendered behavior, keyboard interaction, semantic labels, error states, and accessibility.
  - Continue verifying that the generated client is fresh.
- Instrument identity and Daily Close publication tests use the existing real-PostgreSQL Market Data integration seam.
  - Split stable Instrument identity and provider-run publication into focused test groups.
  - Cover symbol replacement, inactive aliases, cross-Instrument conflicts, rollback, and safe retries.
  - Cover running, succeeded, and failed provider-run transitions.
  - Cover duplicate rows within one run and batch failure.
  - Prove that staged or failed rows remain absent from published reads.
  - Prove that successful completion publishes raw and adjusted close atomically.
  - Retain one end-to-end scenario showing Journal attachment of published Daily Close evidence.
- Edge tests remain focused on adapter composition, downstream failure mapping, authorization, and contract output.
- Existing OpenAPI freshness, authorization parity, migration validation, PostgreSQL role isolation, Docker builds, frontend build, lint, and full-stack release smoke remain required regression gates.
- Add no new test framework. Use the current .NET, Testcontainers, Vitest, Testing Library, MSW, shell smoke, and contract-verification tooling.
- A test that passes after replacing a deep module with a no-op is insufficient. Each module test must prove at least one load-bearing invariant hidden by its interface.
- Avoid duplicating the same behavior at every seam. Focused module tests own the behavior matrix; adapter and release tests prove wiring and the primary user path.

## Out of Scope

- New Market Observation, Expectation, Review, Watchlist, Calendar, Tool, or Settings product features.
- UI redesign, new navigation, new visual language, or new public routes.
- Changing the System Never Judges commitment.
- Embedded AI analysis or system-authored judgement.
- Human-to-Human sharing.
- Moving Access Grants from Journal to Identity.
- Changing fixed or ongoing Access Grant product semantics.
- Changing personal-record mutability or adding record version history.
- Adding personal content to immutable events, outboxes, inboxes, or tombstones.
- Multiple active API Tokens per Agent User.
- A new Agent User ownership model.
- Database-per-process migration.
- Cross-schema reads, writes, foreign keys, triggers, or transactions.
- A monolith rewrite or process split.
- A generic repository, generic workflow engine, or speculative plugin architecture.
- Seams with only one adapter and no demonstrated variation.
- Replacing the generated frontend client.
- A new frontend state-management or form dependency.
- A new browser E2E framework.
- Changing provider credentials, external data-provider integration, or ingestion scheduling.
- Live or intraday market data.
- New markets beyond the existing published coverage.
- Changes to Calculator Presets, Calculation Snapshots, or calculator formulas.
- Modifying historical migrations.
- Opportunistic formatting or unrelated cleanup.

## Further Notes

- This PRD was synthesized from the 2026-07-30 architecture review.
- The top priority is Journal Day because it has a concrete current semantic disagreement, the smallest change surface, and an existing well-tested implementation to deepen.
- Access Grant selection and closure is the highest security and privacy priority after Journal Day.
- The work should increase depth, leverage, and locality. Moving code into more files without shrinking what callers and tests must understand does not satisfy this PRD.
- Apply the deletion test during implementation: if removing a proposed module removes complexity instead of forcing it back into multiple callers, the module is probably a pass-through and should not exist.
- The module interface is the primary test surface. Internal seams are acceptable for implementation tests, but they are not part of the caller contract.
- Existing ADRs remain accepted. This PRD does not require reopening any of them.
