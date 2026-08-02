# Product

## Register

product

## Users

Market participants who independently observe public markets and may trade only occasionally. They follow the United States market first, then Hong Kong, mainland China A-shares, and other markets. They need to preserve what they saw and believed while the context is fresh, often from a phone, without being forced into a daily trading routine.

They are comfortable with market terminology and analytical tools. They value low-friction capture, explicit evidence, and honest review over gamification, generic education, or automated judgment.

## Product Purpose

Micro Cockpit is a **mobile-first market observation and self-review tool**. Its core record is a Market Observation for a Journal Day, not a Trade. A User can quickly capture an Observation Update, distinguish Signals from Interpretations, make a testable Expectation, record an Action Decision, and later review both the Outcome and the quality of the reasoning retained with that Expectation.

Success means that, over time, a User can see which reasoning issues recur, which reasoning strengths are worth repeating, whether actions followed prior decisions, and how those patterns relate to self-reported performance without confusing outcome with process.

Trading activity is optional evidence. Micro Cockpit is explicitly not a brokerage, portfolio, accounting engine, live market terminal, or embedded AI reviewer. It does not route orders, reconstruct holdings, calculate authoritative cost basis, provide real-time quotes, or decide whether a User's market view was correct. Agent Users may access explicitly granted records through the API while preserving ownership boundaries.

## Brand Personality

**Calm. Precise. Restrained.** Three words. The tool feels like a quiet instrument panel a market participant can use quickly on a phone and examine deeply when time permits. It treats written observation, supporting evidence, and numbers with equal seriousness.

It never gamifies, flashes green-red dopamine, congratulates, scolds, or pressures a User to record something every day. It reminds rather than forces the User to preserve an honest account. The voice is plain, adult, and direct.

## Anti-references

- **Consumer trading apps** (Robinhood, meme-broker UIs): confetti, neon gain/loss flashes, streak pressure, dopamine loops. Forbidden.
- **Live market terminals**: dense streaming quotes, intraday monitoring, order entry, and alerts designed to keep the User watching prices.
- **Navy-and-gold "premium fintech"**: dark navy panels, gold accents, faux-luxury. Reads as costume.
- **The 2026 AI warm-cream default**: cream/sand/paper/parchment backgrounds with dusty accents. Generic and off-brief.
- **Generic SaaS dashboards**: card grids and hero metrics that make navigation feel like a feature catalogue.
- **Embedded AI judgment**: automatic diagnoses or opaque claims about the User's reasoning. The product provides records and transparent evidence; AI remains an Agent User or Account Delegate.
- **Decorative motion**: orchestrated page-load reveals, hover choreography, anything that makes the User wait or watch instead of act.

## Design Principles

1. **Observation before transaction.** The product remains useful on days without a Trade. Market context, changing views, and Expectations are the primary material.
2. **Capture first, structure later.** Quick Observation is the lowest-friction action. A User may later enrich it with a subject, Signal, Interpretation, Expectation, evidence, or Action Decision.
3. **Evidence over verdicts.** Reviews separate Outcome, reasoning quality, execution, and performance. Pattern summaries show counts, denominators, and source records rather than issuing diagnoses.
4. **Mobile first.** Every core workflow must work on a phone. Desktop may provide more space, but no core task assumes a large display.
5. **Private by default.** Records are private until the owner creates an explicit, revocable Access Grant. Shared access never transfers ownership or permits edits.
6. **Honesty without coercion.** Personal records may be edited or deleted. The product can warn about retrospective rewriting but does not enforce an immutable audit trail.
7. **Quiet by default.** One restrained accent does the work. No streaks, per-item urgency, or engagement pressure.
8. **Tools support decisions.** Calculators and completed-session market data may provide evidence, but never become trade recommendations or portfolio truth.

## Accessibility & Inclusion

- WCAG 2.2 AA as the floor: body text ≥ 4.5:1, large/bold ≥ 3:1, all interactive targets ≥ 24×24px and preferably 44×44px on touch.
- Full keyboard paths for every action; no hover-only functionality.
- `prefers-reduced-motion` honored; every transition has a still or crossfade fallback.
- Direction and result are never encoded by color alone; always pair color with a sign, icon, or label.
- Numerics are tabular-aligned; money is always labeled with currency.
- English, Traditional Chinese, and Simplified Chinese are first-class interface languages under the existing typed i18n architecture.
