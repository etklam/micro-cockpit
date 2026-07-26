# Market Observation and Self-Review

Status: ready-for-agent

## Problem Statement

Micro Cockpit currently organizes reflection around a daily Trade diary. That model does not fit Market Participants who observe markets frequently but trade only occasionally. Their primary need is to preserve what they noticed, how they interpreted it, what they expected, and how their view changed—not to reconstruct a portfolio or transaction ledger.

The current product also makes it difficult to distinguish a correct Outcome from sound reasoning, compare an Action Decision with later execution, or let an external Agent User analyze explicitly granted records without blurring ownership. Existing supporting tools, market data, and service boundaries are useful, but several product modules duplicate the new observation model or pull the product toward live trading, education, social comparison, or accounting.

The first usable version must replace the unlaunched diary-first model with a mobile-first Market Observation and self-review loop. It must preserve private ownership, support transparent Pattern Review, publish completed-session US Daily Close evidence, and expose pull-based Agent APIs without embedding an AI reviewer.

## Solution

Micro Cockpit will organize each User's record around one Market Observation per Journal Day. A User can create a Quick Observation, add timestamped Observation Updates, distinguish a Signal from an Interpretation, create a testable Expectation, and record an Action Decision. When an Expectation becomes ready for review, the owner separately evaluates its Outcome and retained reasoning quality, then attaches Reasoning Issues and Reasoning Strengths. Pattern Review aggregates those labels with counts, denominators, and links to the currently retained evidence.

The product remains private by default and does not enforce an immutable personal audit trail. Owners may edit or delete their records after a non-blocking Honesty Reminder. Trades and existing calculators remain supporting evidence rather than portfolio truth. Daily Close is limited to completed US sessions and enters through the existing external ingestion boundary.

A human User can provision an Agent User with one revocable API Token and create a fixed or ongoing read-only Access Grant over selected Journal records. The Agent User owns its own market views, initiates filtered or incremental queries, and never edits the human User's records. Account Delegates such as Hermes may continue to act through a human identity and may attach an unverified source label.

## User Stories

1. As a Market Participant, I want to record a market view without recording a Trade, so that the product remains useful on observation-only days.
2. As a Market Participant, I want one Market Observation for each Journal Day, so that related updates retain their daily context.
3. As a Market Participant, I want my Journal Day to use my timezone, so that records follow my actual routine.
4. As a Market Participant, I want to configure the Journal Day rollover time, so that an overnight market session is not split at local midnight.
5. As a Market Participant, I want the default rollover to be local midnight, so that the default is predictable.
6. As a Market Participant, I want empty Journal Days to remain absent, so that inactivity does not create meaningless records.
7. As a mobile User, I want to create a Quick Observation in one focused flow, so that I can preserve a thought before its context fades.
8. As a mobile User, I want Quick Observation to require only text, so that structured fields do not delay capture.
9. As a Market Participant, I want to enrich a Quick Observation later, so that capture and classification happen at different speeds.
10. As a Market Participant, I want each Observation Update to retain its recorded time, so that I can see how my view developed.
11. As a Market Participant, I want one primary Observation Subject, so that search and aggregation have a stable focus.
12. As a Market Participant, I want related Observation Subjects, so that cross-market reasoning retains its context.
13. As a Market Participant, I want subjects for broad markets, sectors, themes, and Instruments, so that I can record views at different levels.
14. As a Market Participant, I want an Instrument to remain the same across ticker changes, so that historical observations stay connected.
15. As a Market Participant, I want US Instruments selected from a system directory, so that they reliably connect to Daily Close data.
16. As a Market Participant, I want to enter Instruments from unsupported markets manually, so that market-data coverage does not restrict reflection.
17. As a Market Participant, I want unsupported Instruments clearly marked as lacking Daily Close, so that absence of evidence is not misleading.
18. As a Market Participant, I want User-defined Tags, so that I can classify events, methods, and market sessions.
19. As a Market Participant, I want Tags kept separate from Observation Subjects, so that contextual labels do not fragment Instrument identity.
20. As a Market Participant, I want to record an optional Mental State, so that later review can include emotional or cognitive context.
21. As a Market Participant, I want the product never to infer my Mental State, so that it does not present speculation as diagnosis.
22. As a Market Participant, I want to record a Signal separately from my Interpretation, so that I can locate where reasoning failed.
23. As a Market Participant, I want to attach a source URL, title, and my own quotation to a Signal, so that later review has supporting context.
24. As a Market Participant, I want the first version to avoid automatic web-page archiving, so that source capture remains predictable and lawful.
25. As a Market Participant, I want to create an Expectation only when a view is testable, so that ordinary observations remain low friction.
26. As a Market Participant, I want an Expectation to state expected behavior, so that its claim is explicit.
27. As a Market Participant, I want an Expectation deadline, so that review is not postponed indefinitely.
28. As a Market Participant, I want an invalidation condition, so that I cannot redefine success after the market moves.
29. As a Market Participant, I want low, medium, or high confidence, so that confidence can be reviewed without false numerical precision.
30. As a Market Participant, I want common deadline presets and a custom date/time, so that both routine and event-driven Expectations are practical.
31. As a Market Participant, I want trading-day presets only for supported market calendars, so that holidays are not guessed incorrectly.
32. As a Market Participant, I want the United States to be the default market, so that the first-version market priority matches my primary use.
33. As a Market Participant, I want to send an Expectation to review early after acknowledging that its invalidation condition occurred, so that I do not wait for a deadline after the thesis has failed.
34. As a Market Participant, I want to change an overdue deadline after seeing an Honesty Reminder, so that the product advises rather than controls my personal record.
35. As a Market Participant, I want ready-for-review Expectations visible in Today and Review, so that unresolved reviews are easy to find.
36. As a Market Participant, I want to classify an Expectation Outcome as confirmed, partially confirmed, invalidated, or indeterminate, so that gray outcomes are represented honestly.
37. As a Market Participant, I want partially confirmed and indeterminate Outcomes to require a short reason, so that ambiguous judgments remain understandable.
38. As a Market Participant, I want reasoning quality classified as sound, mixed, or weak, so that process remains separate from Outcome.
39. As a Market Participant, I want the product never to auto-decide an Expectation Outcome, so that market data remains evidence rather than verdict.
40. As a Market Participant, I want default Reasoning Issue labels, so that Pattern Review works before I create a personal taxonomy.
41. As a Market Participant, I want default Reasoning Strength labels, so that effective habits are visible alongside mistakes.
42. As a Market Participant, I want to add my own Reasoning Issue and Reasoning Strength labels, so that the model adapts to my specific blind spots.
43. As a Market Participant, I want issue and strength labels assessed independently of Outcome, so that luck is not treated as skill.
44. As a Market Participant, I want to record an Action Decision before or alongside action, so that I can compare intention with execution.
45. As a Market Participant, I want an Action Decision to support trade, continue observing, or deliberately avoid trading, so that not trading remains a meaningful decision.
46. As a Market Participant, I want execution classified as followed, partially followed, or deviated, so that adherence is visible without using profit as the standard.
47. As a Market Participant, I want to record a Trade as optional evidence, so that actual action can be linked to prior reasoning.
48. As a Market Participant, I want Trade records not to construct positions or cost basis, so that the product does not become an accounting engine.
49. As a Market Participant, I want weekly, monthly, and custom Pattern Review ranges, so that I can examine both recent and longer-term behavior.
50. As a Market Participant, I want Pattern Review to show counts and denominators, so that repeated labels are not presented without context.
51. As a Market Participant, I want Pattern Review to link to the retained reviews, so that every summary is traceable.
52. As a Market Participant, I want the first version not to diagnose a Confirmed Pattern automatically, so that I remain responsible for interpreting evidence.
53. As a Market Participant, I want to create, disable, and archive Discipline Principles manually, so that I can retain a short list of actionable rules.
54. As a Market Participant, I want to select one Discipline Principle for Today, so that the product does not choose unpredictably among several active principles.
55. As a Market Participant, I want Watchlist to mean continued observation rather than ownership, so that it is not confused with a portfolio.
56. As a Market Participant, I want a short Watchlist Note, so that I can remember why an Instrument remains relevant.
57. As a Market Participant, I want changing views stored as Observation Updates rather than Watchlist Notes, so that history remains in one model.
58. As a Market Participant, I want recent, watched, or active-Expectation Instruments to remain tracked, so that relevant Daily Close evidence continues arriving.
59. As a Market Participant, I want a 30-day tracking window after an Observation Update, so that stale Instruments stop consuming provider capacity.
60. As a Market Participant, I want raw and adjusted US Daily Close values, so that exact price levels and corporate-action-aware comparisons use the correct evidence.
61. As a Market Participant, I want missing Daily Close marked without blocking review, so that provider failures do not stop reflection.
62. As a Market Participant, I want page loads never to call the external market-data provider, so that product usage does not consume provider rate limits.
63. As a Market Participant, I want external ingestion to fetch only tracked Instruments after completed sessions, so that provider usage stays bounded.
64. As a Market Participant, I want all Journal records private by default, so that sharing is always explicit.
65. As a human User, I want to provision an Agent User, so that an external AI can own its own views and analyze mine.
66. As a human User, I want each Agent User to have one active API Token, so that credential management remains simple.
67. As a human User, I want Token replacement to revoke the previous Token immediately, so that rotation has a clear security effect.
68. As a human User, I want Agent Tokens not to expire automatically, so that a long-running personal Agent does not fail unexpectedly.
69. As a human User, I want to see Token creation, last-use, and last-success times, so that I can tell whether an Agent is active.
70. As a human User, I want to revoke an Agent Token, so that I can stop Agent access without deleting my own account.
71. As an Agent User, I want to own my Market Observations and Expectations separately, so that my view is not presented as the human User's view.
72. As an Agent User, I want to manage my own first-version Journal records through the API, so that I can participate as a User rather than an embedded feature.
73. As a human User, I want to create a read-only Access Grant, so that an Agent User can analyze my records without editing them.
74. As a human User, I want first-version grants limited to Agent Users I provisioned, so that arbitrary User sharing and discovery remain out of scope.
75. As a human User, I want a fixed Access Grant, so that I can expose only the complete record closure that existed when I granted access.
76. As a human User, I want an ongoing Access Grant, so that future matching Market Observations and child records become available automatically.
77. As a human User, I want to scope an Access Grant by subject and date range, so that the Agent User receives only relevant records.
78. As a human User, I want an optional Access Grant expiry, so that temporary analysis can end automatically.
79. As a human User, I want to revoke an Access Grant, so that future API access stops immediately.
80. As a human User, I want clear notice that revocation cannot erase external copies, so that the product does not promise remote deletion it cannot enforce.
81. As an Agent User, I want current content for included fixed records, so that edits are visible while the grant remains valid.
82. As an Agent User, I want filtered cursor pagination, so that I can request only the records needed for a task.
83. As an Agent User, I want filters for date, subject, Instrument, Tag, review readiness, and author, so that I control query scope.
84. As an Agent User, I want incremental queries, so that I do not repeatedly download an entire granted history.
85. As an Agent User, I want content-free deletion markers, so that I can remove stale references without receiving deleted content again.
86. As an Agent User, I want a defined cursor-expired response, so that I know when a fresh scoped synchronization is required.
87. As an Account Delegate, I want to use the human User's identity and optionally supply an unverified source label, so that delegated capture can disclose its origin without creating a separate ownership model.
88. As a human User, I want all retrospective edits to remain possible after a warning, so that the product respects personal control.
89. As a human User, I want the system to acknowledge that edited content is only currently retained evidence, so that it does not claim forensic provenance.
90. As a human User, I want permanent record deletion, so that I retain control over my Journal content.
91. As a human User, I want account deletion to remove all User-owned personal content and revoke all grants, so that leaving the product removes my data.
92. As a human User, I want immutable events to exclude all User-entered content, so that replay retention does not defeat deletion.
93. As a human User, I want a complete structured JSON export, so that I can take my first-version records elsewhere.
94. As a human User, I want the first version not to import complete exports, so that ambiguous merge and duplicate behavior is not introduced prematurely.
95. As an anonymous visitor, I want to use existing calculators without registering, so that I can evaluate their utility first.
96. As an authenticated User, I want Calculator Presets and explicitly saved Calculation Snapshots, so that repeated calculations remain convenient.
97. As a User, I want Average Cost and Profit/Loss to remain standalone calculators, so that they do not imply system-owned positions.
98. As a User, I want the legacy Trade Draft removed, so that a planned action is not mistaken for an executed Trade.
99. As a User, I want the first version available in Traditional Chinese and English, so that both supported language audiences can use core workflows.
100. As a User, I want mobile-first touch targets, keyboard access, visible focus, reduced-motion support, and non-color-only meaning, so that core workflows remain accessible.
101. As a User, I want Today, Review, Watchlist, Calendar, Tools, and Settings as the main navigation, so that the interface follows my workflow rather than presenting a feature catalogue.
102. As a User, I want historical observation search under Today, so that a separate archive navigation item is unnecessary.
103. As a User, I want the first version to hide the old Performance model, so that I do not create data in a model scheduled for replacement.
104. As a User, I want Partner Compare, Educational Articles, standalone Research Timeline, and standalone Diary Reminder removed, so that duplicate or unrelated concepts do not remain in the product.
105. As a User, I want the first version not to require offline synchronization, native App support, Webhooks, email, or push notifications, so that the core product can be validated first.

## Implementation Decisions

- Replace diary-first terminology and behavior with the Market Observation domain vocabulary in `CONTEXT.md`.
- Preserve the existing service architecture instead of rewriting as a monolith.
- First-version active boundaries are Identity, Journal, Market Data, Tool, and Edge.
- Identity owns human Users, Agent Users, API Tokens, Token rotation, and Agent usage timestamps.
- Journal owns Journal Days, Market Observations, Observation Updates, Expectations, Expectation Reviews, Reasoning labels, Action Decisions, Trades, Watchlists, Watchlist Notes, Discipline Principles, Access Grants, search, Pattern Review, export, and deletion markers.
- Market Data owns Instrument metadata, symbol history, provider runs, and published Daily Close data.
- Tool continues to own Calculator Presets and Calculation Snapshots.
- Edge remains the public routing and identity-propagation boundary.
- Delete services that exist solely for Partner Compare, Educational Articles, and standalone Research Timeline.
- Retain deferred Performance, Alert, and Rotation capabilities without exposing their old product surfaces in the first version.
- Create a Market Observation only when its first Observation Update is created.
- Determine Journal Day from the User's timezone and rollover; default rollover is local `00:00`.
- Quick Observation creates a text-only Observation Update and performs no automatic classification.
- Observation Updates support one primary subject and zero or more related subjects.
- Instrument identity remains stable across time-bounded symbol changes.
- US Instruments use the system Instrument directory; unsupported markets allow manual market, symbol, and display-name entry without Daily Close.
- First-version Signal evidence supports URL, title, and User-entered quotation. Image upload and automatic page archiving are deferred.
- Expectation confidence is the enum `low | medium | high`.
- Expectation review readiness is the enum `active | ready_for_review | reviewed`.
  - `active`: deadline has not passed and the User has not ended it early.
  - `ready_for_review`: deadline has passed or the User acknowledged early invalidation.
  - `reviewed`: an owner Expectation Review exists.
- Expectation Outcome is `confirmed | partially_confirmed | invalidated | indeterminate`.
- Reasoning quality is `sound | mixed | weak`.
- Partially confirmed and indeterminate Outcomes require an explanation.
- The six default Reasoning Issues are insufficient evidence, contrary evidence ignored, unsupported inference, unsuitable observation horizon, unclear invalidation condition, and confidence miscalibration.
- The six default Reasoning Strengths are sufficient evidence, contrary evidence considered, clear reasoning chain, suitable observation horizon, clear invalidation condition, and proportionate confidence.
- Action Decision intent is `trade | continue_observing | avoid_trade`.
- Execution review is `followed | partially_followed | deviated`.
- Pattern Review supports weekly, monthly, and custom ranges and returns counts, denominators, and evidence links only.
- Confirmed Pattern lifecycle, Pattern-to-Principle conversion, and before/after comparison are deferred.
- Discipline Principles remain manually managed. At most one Principle is selected for Today; selecting another replaces the current selection without archiving either Principle.
- Watchlist membership means continued observation and never creates a position.
- Tracked Instruments include Watchlist members, Instruments with active Expectations, and Instruments observed within the previous 30 days.
- First-version Daily Close supports the United States only and stores raw and adjusted close for completed sessions.
- Preserve the external provider ingestion contract. Market Data does not call providers or store provider credentials.
- The ingestion job queries tracked Instruments, fetches completed-session data once, and pushes a provider run. Product page loads never trigger provider fetches.
- A human User provisions Agent Users through Settings.
- Each Agent User has exactly one active API Token. Generating a Token revokes the previous one.
- Agent Tokens do not expire automatically and expose created, last-used, and last-success timestamps.
- Account Delegate requests may include an optional unverified source label.
- First-version Access Grants are read-only and may target only Agent Users provisioned by the granting human User.
- Access Grants may be fixed or ongoing and may filter by date range and Observation Subject.
- A fixed grant freezes the complete included record closure at grant creation: matching Market Observation IDs and the child record IDs that exist at that time. Later edits to included IDs remain visible, but later-created child records are excluded.
- An ongoing grant evaluates scope continuously: later matching Market Observations and later child records become included.
- The grant closure for a Market Observation may contain Observation Updates, Expectations, Expectation Reviews, Action Decisions, and Trades. It never includes unrelated records owned by the same User.
- Grants may expire and may be revoked. Revocation prevents future access but cannot erase external copies.
- Agent APIs are pull-based. No Webhooks or server push are introduced.
- Agent query APIs support cursor pagination and filters for date, subject, Instrument, Tag, review readiness, and author.
- Incremental cursors remain valid for 90 days. An expired cursor returns `410 cursor_expired`; the Agent User must perform a fresh scoped synchronization.
- Deleted included records return only record ID, record type, and deletion time during the cursor window.
- User-owned Journal records are mutable and have no version history, per ADR-0005.
- Retrospective edits display a non-blocking Honesty Reminder. The system never claims edited content is original evidence.
- Immutable events and consumer inboxes may contain only record ID, record type, version, operation, and event time. They contain no User-entered or personal-record content, per ADR-0006.
- Account deletion removes all User-owned personal content and revokes all grants. Consumers delete or tombstone references after content-free lifecycle events.
- Settings provides a complete structured JSON export of first-version User-owned records. Full import is deferred.
- Keep the existing typed TypeScript i18n architecture with Traditional Chinese and English. Do not redesign it as JSON auto-discovery.
- Keep the existing calculators, anonymous calculation, Calculator Presets, and explicitly saved Calculation Snapshots.
- Average Cost and Profit/Loss remain standalone. Action Decision Draft integration is deferred.
- Remove the legacy Trade Draft workflow permanently; Action Decision Draft is a distinct deferred concept.
- Main navigation is Today, Review, Watchlist, Calendar, Tools, and Settings.
- Historical search is available from Today instead of a separate archive navigation item.
- First-version Calendar is observation-focused. The replacement Performance model and Calendar Lens switch are deferred.
- First version is a mobile-first responsive Web application. Native App and offline synchronization are deferred.
- First-version reminders are limited to visible ready-for-review items in Today and Review; scheduled reminders are deferred.

## Testing Decisions

- Tests must assert externally observable behavior through existing seams rather than private methods, internal class shapes, or duplicated calculation details.
- Prefer one high-level golden path plus a small number of integration branch tests. Do not create a unit-test suite for every aggregate.
- Use the existing full-stack Edge release smoke as the highest test seam.
  - Add one first-version golden path spanning Human User authentication, Quick Observation, Expectation, Action Decision, Watchlist, external Daily Close ingestion, Expectation Review, Pattern Review, Agent User provisioning, Access Grant, Agent-owned CRUD, incremental query, edit visibility, and content-free deletion markers.
  - Verify removed public product surfaces no longer resolve.
  - Verify existing anonymous calculators still resolve.
- Use existing Journal and Identity service integration tests based on in-process application hosting with a real PostgreSQL container.
  - Journal integration tests own the behavior matrix for Journal Day rollover, Observation lifecycle, validation, review readiness, required explanations, Pattern Review denominators, search filters, mutable records, Honesty Reminder response state, fixed and ongoing grant closure, expiry, revocation, ownership isolation, incremental cursor behavior, deletion markers, export completeness, Discipline Principle selection, and content-free lifecycle events.
  - Identity integration tests own Agent User provisioning, managing-human authorization, one-active-Token replacement, no automatic Token expiry, revocation, and usage timestamps.
  - Edge endpoint tests remain limited to routing, coarse policy, identity propagation, and preservation of downstream filter, cursor, and error contracts.
- Use existing frontend Vitest, Testing Library, user-event, jsdom, and MSW seams.
  - Cover Quick Observation and progressive enrichment as screen-level behavior.
  - Cover Expectation creation, validation, early invalidation, review controls, and required explanations.
  - Cover Honesty Reminder visibility while confirming that saving remains possible.
  - Cover Today, Review, Watchlist, Calendar, Tools, and Settings navigation.
  - Cover English and Traditional Chinese core workflows.
  - Cover absence of old Performance, Partner Compare, Educational Articles, standalone Research Timeline, standalone Diary Reminder, and Trade Draft surfaces.
  - Cover semantic labels, keyboard interaction, visible states, and non-color-only meaning where jsdom can observe them.
- Continue using migration verification as a schema and deployment guardrail, not as a fourth product behavior seam.
  - Verify Human/Agent ownership constraints, Access Grant references, one-active-Token invariant, deletion-marker storage, content-free event constraints, Instrument symbol history, and raw/adjusted Daily Close publication views.
  - Do not duplicate API CRUD or Pattern Review behavior in migration tests.
- Do not add Playwright, Cypress, or a second browser E2E framework in the first version.
- Mobile layout, actual touch-target dimensions, overflow, virtual keyboard behavior, and full WCAG conformance require manual responsive QA for the first version. Add one browser-level mobile golden path only if jsdom proves unable to prevent a concrete regression.
- Daily Close publication should reuse the existing provider-run smoke pattern: data is invisible before successful completion and visible after publication.
- Deletion tests must inspect both public API output and persisted immutable event/inbox payloads to prove that no User-entered content survives there.

## Out of Scope

- Embedded AI analysis, automatic Outcome decisions, automatic reasoning diagnoses, or AI-generated Pattern declarations.
- Commentary, External Assessment, and Analysis Inbox.
- Human User-to-Human User sharing or discovery.
- Multiple active API Tokens per Agent User.
- Agent Token auto-expiry.
- Webhooks or server-initiated Agent notifications.
- Full request-by-request Agent audit logs.
- Image upload, EXIF removal, thumbnails, object storage, or arbitrary file attachments.
- Automatic source-page scraping or archival.
- Full data import or merge behavior.
- Native mobile App.
- Offline capture or synchronization.
- Email, browser push, or scheduled reminder delivery.
- Multi-market or multi-currency Performance Entry.
- The old single-value daily Performance model.
- Observation/performance Calendar Lens switching.
- Close Alert.
- Market Rotation Snapshot integration.
- Hong Kong and mainland China A-share Daily Close.
- User-defined Market Rotation universes.
- Confirmed Pattern lifecycle and Pattern status tracking.
- Pattern-to-Discipline-Principle conversion.
- Before/after Discipline Principle comparison.
- Action Decision Draft and calculator-to-record attachment.
- Position, holdings, portfolio, cost-basis, order, fill, or accounting models.
- Live or intraday market data.
- Investment education content.
- Partner Compare.
- Standalone Research Timeline.
- Standalone Diary Reminder.
- Legacy Trade Draft.
- A new i18n architecture.
- A monolith rewrite.
- A new browser test framework.

## Further Notes

- The system is not live, so no production data migration or compatibility layer is required. Existing diary-first tables and routes may be replaced rather than preserved for users.
- The glossary in `CONTEXT.md` is the source of truth for domain vocabulary.
- ADR-0004 records the separation between Agent User identity and Journal-owned Access Grants.
- ADR-0005 records the deliberate choice to keep personal Journal records mutable without version history.
- ADR-0006 prevents immutable event retention from preserving deleted personal content.
- Existing ADRs requiring service-owned schemas, no runtime cross-schema writes, and immutable replay history remain in force.
- Market priority remains United States, then Hong Kong, mainland China A-shares, then other markets. Only US Daily Close is part of this spec.
- Performance, Close Alert, Market Rotation Snapshot, Commentary, External Assessment, image evidence, and native App support remain approved later concepts, but they must not block this first version.
