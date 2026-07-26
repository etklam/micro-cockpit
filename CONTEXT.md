# Market Reflection

Micro Cockpit helps market participants preserve their view of the market and examine how that view develops over time. Trading activity may support the reflection, but is not required.

## Observation

**Journal Day**:
A personal daily period defined by a User's timezone and chosen rollover time.
_Avoid_: UTC date, exchange date

**Market Observation**:
A private personal record of what a User noticed, believed, or expected during one Journal Day. It may exist without any Trade.
_Avoid_: Trade log, trading record

**Observation Update**:
A timestamped addition to a Market Observation that captures the User's view at a particular moment.
_Avoid_: Final daily summary

**Observation Subject**:
A market, sector, theme, or Instrument discussed by an Observation Update.
_Avoid_: Instrument name as identity, Tag

**Signal**:
A fact, event, or market condition noticed by a User without attaching a conclusion to it.
_Avoid_: Interpretation, prediction

**Interpretation**:
The meaning a User assigns to one or more Signals.
_Avoid_: Signal, Expectation

**Tag**:
A User-defined contextual label such as an event, analytical method, or market session.
_Avoid_: Instrument identity, reasoning assessment

**Mental State**:
A User-defined description of their emotional or cognitive condition during an observation, decision, or review.
_Avoid_: Diagnosis, mood score

## Expectations and review

**Expectation**:
A testable market view with an observation horizon, an invalidation condition, and an explicitly stated confidence level.
_Avoid_: Unbounded prediction, ordinary observation

**Expectation Outcome**:
The reviewed result of an Expectation: confirmed, partially confirmed, invalidated, or indeterminate.
_Avoid_: Reasoning quality

**Expectation Review**:
The Expectation owner's assessment of its Outcome and of the retained reasoning associated with it.
_Avoid_: Automatic verdict, outcome-only score, another User's assessment

**External Assessment**:
A User's independently owned assessment of another User's shared Expectation. It never replaces the owner's Expectation Review.
_Avoid_: Official review, overwrite

**Reasoning Issue**:
A predefined or User-defined label identifying a repeatable flaw in the reasoning behind an Expectation.
_Avoid_: Outcome, unstructured note

**Reasoning Strength**:
A predefined or User-defined label identifying a sound reasoning behavior worth repeating, independent of the Outcome.
_Avoid_: Correct Outcome, praise without evidence

**Confirmed Pattern**:
A recurring Reasoning Issue or Reasoning Strength that the User has explicitly accepted as meaningful after reviewing its frequency and evidence.
_Avoid_: Automatically inferred problem

**Discipline Principle**:
A short, actionable rule a User chooses to apply in future observation or decision-making, often in response to a Confirmed Pattern.
_Avoid_: Problem label, motivational slogan

## Decisions and results

**Action Decision**:
A timestamped statement of a User's intended response to an Observation Update or Expectation.
_Avoid_: Trade, retrospective explanation

**Trade**:
A trading action optionally connected to the observation, Expectation, or Action Decision that informed it. It is evidence of action, not a holdings or accounting record.
_Avoid_: Primary journal record, order lifecycle, portfolio transaction

**Performance Entry**:
A self-reported result for one Journal Day and currency or market, separate from reasoning quality and from authoritative portfolio accounting.
_Avoid_: Derived portfolio P/L, automatic FX accounting

## People and access

**User**:
A human or AI account that can own Market Observations and Expectations. Ownership identifies whose view a record represents.
_Avoid_: Human-only User

**Market Participant**:
A human User who independently observes markets and may occasionally make trading decisions. Frequent trading is not required.
_Avoid_: Active trader only, day trader

**Agent User**:
An AI account provisioned by a human User, with its own identity and separately owned market views.
_Avoid_: Built-in AI feature, anonymous integration, shared human identity

**Account Delegate**:
An external agent permitted to use a human User's identity directly. Its actions belong to that human User even when it self-reports its origin.
_Avoid_: Agent User, independently owned view

**Access Grant**:
Revocable read permission allowing one User to access selected records owned by another User without transferring ownership.
_Avoid_: Ownership transfer, shared identity, edit permission

**Commentary**:
A User's separately owned response, suggestion, or analysis attached to another User's shared record.
_Avoid_: Edit, correction of another User's record

## Markets and tools

**Instrument**:
A stable tradable-security identity that remains continuous across display-name or symbol changes.
_Avoid_: Free-text symbol, display name as identity, current symbol as permanent identity

**Default Market**:
The market a User normally observes when an Expectation does not identify another market.
_Avoid_: Mandatory market on every Expectation

**Daily Close**:
Completed-session price evidence for an Instrument.
_Avoid_: Live quote, intraday price

**Watchlist**:
A User's explicit list of Instruments selected for continuing observation. Membership does not imply ownership, a position, or intent to trade.
_Avoid_: Portfolio, holdings, buy list

**Watchlist Note**:
A short explanation of why a User continues to watch an Instrument.
_Avoid_: Research timeline, Expectation, long-form journal

**Tracked Instrument**:
An Instrument that remains relevant because of a recent Observation Update, an active Expectation, or Watchlist membership.
_Avoid_: Every historical Instrument

**Market Rotation Snapshot**:
A completed-session view of relative strength and breadth within a defined market universe.
_Avoid_: Live rotation monitor, trade signal

**Calculation Snapshot**:
The preserved inputs and outputs of a small analytical tool at a point in time.
_Avoid_: Live market data, authoritative portfolio value
