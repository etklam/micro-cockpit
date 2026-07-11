# Product

## Register

product

## Users

Solo retail traders and serious part-time traders. They use the cockpit alone,
usually at the end of a session — evening, low light, often a multi-monitor
desk — to do the thing the market didn't let them do while trading: reflect.
Their context is post-adrenaline: they just made decisions under pressure and
now want to capture what happened, what they noticed, and what they'll remember
next time, before the detail fades. They are fluent in trading tools and have
zero patience for friction, decoration, or being treated like a beginner.

## Product Purpose

Micro Cockpit is a **diary-first trade journal**. It exists to make reflection
the default, not an afterthought: a trader records the day's decisions as a
diary, logs the trades behind them, marks daily P/L on a calendar, keeps a
short list of personal discipline principles (one surfaces each day), and sets
reminders to write. Success looks like a trader who, over months, can see the
relationship between their discipline, their decisions, and their results — and
who trusts the tool enough to be honest in it. It is explicitly **not** a
brokerage, portfolio, or accounting engine: no holdings, no cost basis, no
real-time quotes. The numbers are self-reported P/L, in service of reflection.

## Brand Personality

**Calm. Precise. Restrained.** Three words. The tool feels like a quiet
instrument panel a solo operator reads in low light — graphite and ink with a
single signal lamp. It treats the trader's written reflection and their numbers
with equal seriousness. It never gamifies, never flashes green-red dopamine,
never congratulates or scolds. It is the opposite of a consumer trading app: no
confetti, no streaks-as-pressure, no hot tips. It rewards honesty and returns
discipline to the trader each morning. The voice is plain, adult, and direct.

## Anti-references

- **Consumer trading apps** (Robinhood, meme-broker UIs): confetti, neon
  gain/loss flashes, streak pressure, dopamine loops. Forbidden.
- **Navy-and-gold "premium fintech"** (the Bloomberg-clone / private-bank
  cliché): dark navy panels, gold accents, faux-luxury. Reads as costume.
- **The 2026 AI warm-cream default**: cream/sand/paper/parchment backgrounds
  with dusty accents. Generic and off-brief.
- **Generic SaaS dashboard**: the current build. Flat blue sidebar, identical
  card grids, eyebrow-on-everything, hero-metric tiles. What we are replacing.
- **Decorative motion**: orchestrated page-load reveals, hover choreography,
  anything that makes the trader wait or watch instead of act.

## Design Principles

1. **Reflection and numbers, equal weight.** The diary prose and the P/L are
   both first-class. Neither is decoration for the other. Prose gets a real
   reading measure; numbers get tabular precision.
2. **Quiet by default.** One restrained accent does all the work. Surfaces are
   deep and calm; state is signalled subtly, not shouted. Negative space is the
   dominant material.
3. **Instrument, not stage.** The tool disappears into the task. Every screen
   is dense only where density serves the task, empty where it doesn't.
   Familiar affordances, consistent vocabulary screen to screen.
4. **Honesty over performance.** Copy and framing never congratulate, shame, or
   pressure. "You showed up today" is as close to praise as it gets.
5. **Reward opening it.** The daily discipline principle and the quick-note
   capture sit at the top of the day. The hardest part — starting to write — is
   the lowest-friction thing on screen.

## Accessibility & Inclusion

- WCAG 2.2 AA as the floor: body text ≥ 4.5:1, large/bold ≥ 3:1, all
  interactive targets ≥ 24×24px (44px on touch), visible focus-visible rings.
- Full keyboard paths for every action; no hover-only functionality.
- `prefers-reduced-motion` honored — every transition has a still or
  crossfade fallback.
- P/L direction never encoded by color alone (red/green) — always paired with a
  sign, an arrow, or a label, for red-green color blindness.
- Numerics tabular-aligned; money always labeled with currency.
