# DESIGN.md — Diary-first Trade Journal UI/UX Guide

## 1. Product Direction

This product is a **Diary-first Trade Journal** for early-stage investors.

It is not a generic market terminal, not a portfolio accounting system, and not a Bloomberg-style dashboard.

The product helps the user build a decision loop:

```text
Observe market
→ Write Diary
→ Record optional Transaction
→ Record Daily P/L
→ Review later
→ Improve Discipline
```

Primary product areas:

```text
Diary
Quick Note
Transaction
Daily P/L Calendar
Discipline
Diary Alert
Watchlist
Stock Note
Stock Timeline Record
Price Alert
Partner
AI Agent Partner
Post
Tools
Market Rotation Monitor
```

Design target:

```text
Calm dark notebook
Disciplined investing workflow
Low-noise financial dashboard
Mobile-friendly daily capture
```

Avoid:

```text
Crypto casino feeling
Professional trader terminal overload
Landing page hero sections inside the app
Excessive neon green/red
Generic admin dashboard layout
```

---

## 2. Core Product Principles

## 2.1 Diary Is the Center

The UI should make it obvious that Diary is the core.

Dashboard should answer:

```text
Did I write today?
What did I observe?
Did I trade?
What was today's P/L?
What do I need to review?
What discipline should I remember?
```

Market Rotation Monitor and Watchlist are supporting tools.  
They should feed the user's thinking, but the final memory should live in Diary.

## 2.2 Mobile Is for Habit Formation

Mobile is not a mini desktop.

Mobile primary actions:

```text
Quick Note
Create Diary
Record Transaction
Input Daily P/L
Check Diary Alerts
Review Discipline
```

Mobile should optimize:

```text
Capture
Check
Reflect
Review
```

Not:

```text
Large tables
Heavy chart analysis
Dense sector matrices
```

## 2.3 Dark Mode First

Dark mode is the primary experience.

Reasons:

```text
- Long reading/writing sessions
- Better chart/card contrast
- More comfortable for post-market review
- Fits investment journaling mood
```

Light mode may exist, but dark mode should be the polished default.

## 2.4 New Investor Friendly

Target user has 0–3 years investing experience.

Design implication:

```text
- Use explanation text where useful
- Avoid unexplained signal overload
- Use progressive disclosure
- Do not show every metric at once
- Prefer "what changed" and "what needs action" over raw data dump
```

## 2.5 Do Not Gamify P/L

Daily P/L is useful, but should not turn the app into a casino.

Rules:

```text
- Use calm colors
- Avoid confetti
- Avoid streak addiction mechanics
- Avoid ranking days like a game
- Emphasize process review, not only outcome
```

---

## 3. Information Architecture

## 3.1 Desktop Primary Navigation

```text
Dashboard
Diary
Calendar
Quick Note
Watchlist
Discipline
Diary Alerts
Price Alerts
Partners
Articles
Tools
Admin
```

## 3.2 Tools Navigation

```text
Market Rotation Monitor
Position Sizing
Risk / Reward
FIRE
Relative Value
Seasonality
```

## 3.3 Mobile Bottom Navigation

```text
Dashboard
Diary
Quick Note
Calendar
More
```

`More` contains:

```text
Watchlist
Discipline
Diary Alerts
Price Alerts
Partners
Articles
Tools
Settings
Admin
```

## 3.4 Route Structure

```text
/today
/diary
/diary/:diaryId
/calendar/:year/:month
/discipline
/alerts
/more
/watchlist
/price-alerts
/rotation
/partners
/articles
/articles/:slug
/tools
```

Quick Note is an action in the diary-first workflow rather than a separate route. Unknown routes show the not-found state.

---

## 4. Layout System

## 4.1 Desktop App Shell

```text
┌──────────────┬──────────────────────────────────────────────┬──────────────────────┐
│ Sidebar      │ Top Bar                                      │ Right Context Panel  │
│              ├──────────────────────────────────────────────┤                      │
│ Navigation   │ Main Workspace                               │ Alerts / Discipline  │
│              │                                              │ Diary Context        │
│ Today status │ Diary / Calendar / Watchlist / Tools         │ Stock Context        │
└──────────────┴──────────────────────────────────────────────┴──────────────────────┘
```

Recommended widths:

```text
Sidebar: 240px
Right context panel: 320px - 380px
Main content: flexible
```

## 4.2 Mobile App Shell

```text
┌──────────────────────────┐
│ Compact Header           │
├──────────────────────────┤
│ Page Content             │
├──────────────────────────┤
│ Sticky Primary Action    │
├──────────────────────────┤
│ Bottom Navigation        │
└──────────────────────────┘
```

Mobile rules:

```text
- No permanent sidebar
- No dense desktop tables
- Use card lists
- Use bottom sheets for forms and filters
- Primary action is obvious
- Quick Note is one tap away
```

## 4.3 Right Context Panel

Context panel changes by page.

Dashboard:

```text
Today's Discipline
Pending Diary Alerts
Recent Quick Notes
```

Diary editor:

```text
Linked stocks
Transactions
Daily P/L for the date
Diary Alert settings
Related past diaries
```

Calendar:

```text
Selected day summary
Diary entries
Transactions
Daily P/L edit
Diary Alerts
```

Stock page:

```text
Current Stock Note
Price Alerts
Related Diary entries
Timeline Records
```

Market Rotation Monitor:

```text
Market State explanation
Sector Breadth explanation
Rank Scope filter
Data freshness
```

---

## 5. Theme System

## 5.1 Dark Mode Tokens

v0.1 ships **dark-only** (committed dark). Calm near-black, not pure black — a
whisper of the brand violet. The accent is violet (hue 279), replacing the
earlier generic blue per the "quiet cockpit" direction.

**Source of truth: `frontend/src/index.css`** (OKLCH). Contrast verified WCAG AA
(ink→bg 16.5:1, muted→bg 7.35:1, primary-button label 6.17:1).

```css
/* surfaces — hue 279, near-black with a whisper of violet */
--bg:           oklch(0.150 0.008 279);   /* app background */
--surface:      oklch(0.195 0.010 279);   /* cards, panels */
--surface-2:    oklch(0.165 0.009 279);   /* inputs, inset wells */
--overlay:      oklch(0.230 0.011 279);   /* hover / raised */

/* text */
--ink:          oklch(0.94 0.004 279);    /* body */
--muted:        oklch(0.70 0.012 279);    /* secondary, labels */
--faint:        oklch(0.50 0.014 279);    /* large / decorative only */

/* brand — violet */
--primary-btn:  oklch(0.50 0.17 279);     /* filled primary button */
--primary:      oklch(0.62 0.16 279);     /* accents, links, focus, active nav */

/* P/L — restrained, never neon; direction always paired with a +/- sign */
--gain:         oklch(0.72 0.14 155);
--loss:         oklch(0.66 0.17 25);
--warn:         oklch(0.78 0.14 85);      /* active alerts */

/* lines adapt via color-mix(in oklch, var(--ink) N%, transparent) */
```

## 5.2 Light Mode Tokens

Not implemented in v0.1 (dark-only). Kept as a provisional future reference; if
implemented later, re-derive from the same violet brand seed on a true off-white.

```css
--bg-app: #F6F3EC;
--bg-surface: #FBF8F1;
--bg-surface-elevated: #FFFFFF;
--bg-card: #FFFDF8;
--bg-card-hover: #F8F4EA;

--border-subtle: #E5DED2;
--border-strong: #CDBFAE;

--text-primary: #1F2933;
--text-secondary: #52606D;
--text-muted: #7B8794;

--accent-primary: #2563EB;

--profit: #16803C;
--profit-soft: rgba(22, 128, 60, 0.10);

--loss: #C2410C;
--loss-soft: rgba(194, 65, 12, 0.10);

--warning: #B7791F;
--neutral: #64748B;
```

## 5.3 P/L Color Rules

Daily P/L should be visible but not emotionally aggressive.

```text
Profit day:
- subtle green background
- green text for amount
- arrow up icon optional

Loss day:
- subtle red background
- red text for amount
- arrow down icon optional

Flat day:
- neutral slate background
- muted text

No data:
- default card background
- no color
```

Do not use:

```text
Pure bright green background
Pure bright red background
Flashing colors
Streak badges
Confetti
```

---

## 6. Typography

Recommended font stack:

```css
font-family:
  Inter,
  ui-sans-serif,
  system-ui,
  -apple-system,
  BlinkMacSystemFont,
  "Segoe UI",
  sans-serif;
```

Use tabular numbers:

```css
font-variant-numeric: tabular-nums;
```

Apply to:

```text
Prices
P/L
Percentages
Ranks
Scores
Calendar day values
```

Type scale:

```text
Page title: 24px / 32px / 700
Section title: 16px / 24px / 650
Card title: 13px / 20px / 700
Body: 14px / 22px / 400
Small: 12px / 18px / 400
Data large: 32px / 40px / 600
Data medium: 20px / 28px / 600
Data small: 13px / 18px / 600
```

---

## 7. Core Components

Required components:

```text
AppShell
Sidebar
TopBar
RightContextPanel
MobileBottomNav
CommandPalette

DashboardSummary
TodayDiaryCard
QuickNoteCard
DailyPnlCard
MonthlyPnlCard
CalendarMonthGrid
CalendarDayCell
MobileCalendarList
SelectedDayPanel

DiaryTimeline
DiaryEditor
TransactionEditor
DailyPerformanceEditor
DisciplineCard
RandomDisciplineCard

DiaryAlertList
PriceAlertList
WatchlistTable
WatchlistCard
StockHeader
StockNotePanel
StockTimelinePanel

MarketStateCard
SectorBreadthCard
MarketRotationTable
RotationSignalPill
RankDeltaBadge

StatusPill
PriorityBadge
ProfitLossBadge
Sparkline
EmptyState
LoadingSkeleton
ErrorState
```

---

## 8. Dashboard Design

Dashboard is not a data dump. It is a daily action summary.

Desktop sections:

```text
Top row:
- Today Diary
- Daily P/L
- Pending Diary Alerts

Middle row:
- Quick Note
- Random Discipline
- Watchlist Changes

Lower row:
- Market Rotation Summary
- Price Alerts
- Recent Diary Entries
```

Mobile order:

```text
1. Quick Note
2. Today Diary status
3. Daily P/L input/status
4. Pending Diary Alerts
5. Today's Discipline
6. Watchlist Changes
7. Market Rotation Summary
```

Dashboard should answer:

```text
What should I record today?
What happened today?
Did I make or lose money?
What do I need to review?
What discipline should I remember?
Is the market condition changing?
```

---

## 9. Diary Design

## 9.1 Diary List

Diary list should use timeline grouping.

Display:

```text
Date
Title
Short excerpt
Linked stocks
Transaction count
Daily P/L badge
Diary Alert status
Tags
```

## 9.2 Diary Editor

Desktop:

```text
Main editor + right metadata panel
```

Right metadata panel includes:

```text
User local date
Linked stocks
Transactions
Daily P/L for the date
Diary Alert settings
Tags
```

Mobile:

```text
Single column editor
Bottom sheet for metadata
Sticky Save button
```

## 9.3 Diary Fields

```text
Title
Diary date
Content
Market observation
Thesis
Risk
Execution
Conclusion
Mood
Tags
Linked stocks
Transactions
Diary Alert
```

Do not make every field mandatory.  
Diary must remain low-friction.

---

## 10. Quick Note Design

Quick Note is a fast input path into Diary.

Templates:

```text
Free Writing
Trading Diary
Post-market Reflection
Market Observation
```

Mobile Quick Note:

```text
Large text input
Template chips
Optional stock link
Optional transaction quick add
Save as a new Diary
Optional: choose an existing Diary and append
```

Rules:

```text
- Quick Note should not feel like a full form.
- With no selected Diary, it creates a new Diary for the selected local date.
- With an explicitly selected Diary, it appends to that Diary's content only.
- It never guesses among multiple same-day Diaries.
- It should be usable in under 30 seconds.
```

---

## 11. Daily P/L Calendar Design

## 11.1 Product Purpose

Daily P/L Calendar connects outcome with decision process.

It should help the user review:

```text
Which days did I make or lose money?
What did I write on those days?
Did I trade?
Was the P/L caused by process or noise?
```

This feature must not become full portfolio accounting.

## 11.2 Desktop Month Calendar

Each day cell shows:

```text
Day number
Daily P/L amount
Daily P/L %
Diary count dot
Transaction count icon
Diary Alert / Review marker
```

Example cell:

```text
05
+$320
+0.42%
2 diaries · 1 tx
```

Day cell states:

```text
Profit day
Loss day
Flat day
No P/L data
Today
Selected day
Weekend
Future day
```

## 11.3 Desktop Calendar Layout

```text
┌──────────────────────────────────────────────┐
│ July 2026        Total P/L +$1,240  +2.8%    │
├──────┬──────┬──────┬──────┬──────┬──────┬──────┤
│ Mon  │ Tue  │ Wed  │ Thu  │ Fri  │ Sat  │ Sun  │
├──────┼──────┼──────┼──────┼──────┼──────┼──────┤
│ Day cells with P/L, diary count, tx count        │
└──────────────────────────────────────────────┘
```

Side panel for selected day:

```text
Selected date
Daily P/L editor
Diary entries
Transactions
Diary Alerts
Discipline note
```

## 11.4 Mobile Calendar

Mobile should not force a full dense month grid.

Recommended mobile layout:

```text
Month summary card
Week strip
Daily P/L list
Selected day bottom sheet
```

Mobile month summary:

```text
July 2026
Total P/L: +$1,240
Win days: 9
Loss days: 5
Best day: +$520
Worst day: -$310
```

Daily list:

```text
Jul 05   +$320   +0.42%   2 diary · 1 tx
Jul 04   -$80    -0.10%   1 diary
```

## 11.5 Daily P/L Input

Manual input first.

Fields:

```text
P/L amount
P/L percent, read-only and derived when capital base is present
Currency
Capital base optional
Note optional
Source read-only or hidden
```

Rules:

```text
- Manual Daily P/L is the first version.
- P/L amount uses the user's base currency.
- The server derives P/L percent; the user does not enter an independent conflicting value.
- Do not calculate portfolio mark-to-market.
- Do not require holdings.
- Do not require cost basis.
- Do not require brokerage import.
```

## 11.6 Monthly Summary

Monthly summary card:

```text
Total P/L
Total P/L %
Win days
Loss days
Flat days
Best day
Worst day
Average winning day
Average losing day
Diary completion days
```

Use this as reflection support, not leaderboard.

---

## 12. Transaction Design

Transaction is inside Diary.

Transaction editor fields:

```text
Symbol
Asset type: stock / etf
Side: buy / sell
Quantity
Price
Currency
Transaction time
Notes
```

Rules:

```text
- Transaction must belong to Diary.
- Transaction is not an order.
- Transaction is not portfolio accounting.
- Transaction should be quick to add from Diary editor.
```

UI should show:

```text
This transaction belongs to: Diary date/title
```

---

## 13. Discipline Design

Discipline is a trading lesson list.

UI sections:

```text
Random Discipline
All Disciplines
Recently added
```

Random card example:

```text
Today's Discipline
Do not change your thesis because of one red candle.
```

`Today's Discipline` is stable for the signed-in user's local date. The separate Discipline-center random action may return a different item on each request.

Rules:

```text
- No daily checklist
- No completion streak
- No DisciplineCheck
- Random extraction is a primary interaction
```

---

## 14. Diary Alert and Price Alert Design

## 14.1 Diary Alert

Diary Alert reminds the user to return to a specific Diary.

Display:

```text
Diary title
Reminder date/time
Repeat mode: None / Week / Month
Status
```

Repeat mode helper text:

```text
Week: remind on weekdays until Friday
Month: remind on weekdays until month end
```

## 14.2 Price Alert

Price Alert is stock-price driven.

Display:

```text
Stock symbol
Condition
Threshold
Last price
Status
```

Rules:

```text
- Do not mix Diary Alerts and Price Alerts in one undifferentiated list.
- Use separate pages or tabs with clear naming.
```

---

## 15. Watchlist / Stock Design

## 15.1 Watchlist

Watchlist is stocks only.

List display:

```text
Symbol
Name
Current view summary
Price movement
Price Alert badge
Last Stock Note update
Timeline count
```

## 15.2 Stock Page

Stock page sections:

```text
Stock header
Current Stock Note
Stock Timeline Records
Related Diary entries
Price Alerts
```

Do not show ETF Market Rotation Monitor inside Stock page.

## 15.3 Stock Note

Stock Note is mutable current view.

UI should make it feel like the current thesis header.

## 15.4 Stock Timeline Record

Timeline Record is immutable evidence.

UI should show:

```text
Event time
Source type
Title
Content
Linked Diary / AI Agent source if available
```

Existing Timeline Records cannot be edited or deleted. A correction is a new append-only Timeline Record linked to the original record, never an update of existing evidence.

---

## 16. Market Rotation Monitor Design

Market Rotation Monitor lives under Tools.

Route:

```text
/tools/market-rotation-monitor
```

It can feed Dashboard summary, but it is not the product center.

Sections:

```text
Market State
Sector Breadth
Breadth Confirmation
Sector Rotation Matrix
Indexes Comparison
Core ETF Scope
2W Trend
Batch/Data Freshness
```

Rules:

```text
- Use canonical labels from backend.
- Explain rank scope.
- Do not mix stocks into ETF rotation.
- Do not show missing data as neutral.
- Current Market Summary must be deterministic and match displayed data.
```

Mobile design:

```text
Market State card
Sector Breadth card
Top improving sectors
Top weakening sectors
Expandable ETF cards
```

---

## 17. Partner / AI Agent Partner Design

Partner is peer sharing.

UI sections:

```text
Partners
Sharing settings
Shared Diaries
Shared Stock Notes
Partner Compare
```

Rules:

```text
- Avoid "followers" language.
- Avoid social network feel.
- AI Agent Partner should appear as a partner identity, not a separate bot sidebar.
```

AI Agent Partner display:

```text
Name
Account type: AI Agent Partner
Shared stock notes
Shared timeline records
Last update
```

---

## 18. Post / Articles Design

Post is public educational content.

Rules:

```text
- Post is not Diary.
- Public article pages should not expose author email.
- Admin editor is separate from Diary editor.
```

Routes:

```text
/articles
/articles/:slug
/admin/posts
/admin/posts/:id
```

---

## 19. Accessibility

Minimum requirements:

```text
- WCAG AA contrast
- Price movement not color-only
- P/L movement not color-only
- Visible focus states
- Keyboard-accessible command palette
- Touch target minimum 44px
- Calendar cells usable with keyboard
- Screen reader labels for profit/loss
```

Calendar screen reader examples:

```text
July 5, profit 320 dollars, 2 diaries, 1 transaction
July 6, loss 80 dollars, no transaction
```

---

## 20. Responsive Breakpoints

```text
Mobile: < 768px
Tablet: 768px - 1023px
Desktop: 1024px - 1439px
Wide: >= 1440px
```

Behavior:

```text
Mobile:
- bottom nav
- card lists
- no dense month grid by default
- use bottom sheet

Tablet:
- collapsible sidebar
- calendar can use compact grid

Desktop:
- full sidebar
- full calendar grid
- optional right context panel

Wide:
- persistent right panel
- richer dashboard grid
```

---

## 21. Motion and Interaction

Allowed:

```text
Bottom sheet slide
Panel slide
Tab transition
Card hover
Skeleton loading
Calendar day selection
```

Avoid:

```text
Animated P/L celebration
Flashing red/green
Heavy animated gradients
Chart animation on every refresh
Distracting glow pulse
```

---

## 22. Empty / Loading / Error States

Every major page must support:

```text
Loading skeleton
Empty state
Error state
Retry action
```

Calendar empty state:

```text
No P/L recorded this month.
Start by entering today's Daily P/L or writing a Diary.
[Input Daily P/L] [Create Diary]
```

Diary empty state:

```text
No diary entries yet.
Start with a Quick Note to record today's market observation.
[Quick Note]
```

Watchlist empty state:

```text
Your Watchlist is empty.
Add stocks you want to follow and maintain a current Stock Note.
[Add Stock]
```

Market Rotation error state:

```text
Market Rotation data is unavailable.
Last successful update: 2026-07-05 09:30
[Retry]
```

---

## 23. Design Acceptance Checklist

```text
[ ] Diary is visually and navigationally central
[ ] Quick Note is one tap away on mobile
[ ] Daily P/L Calendar exists
[ ] Calendar does not imply full portfolio accounting
[ ] Daily P/L can be manually entered
[ ] Calendar links P/L with Diary and Transactions
[ ] Mobile Calendar uses summary/list, not cramped desktop grid
[ ] Transaction is displayed inside Diary context
[ ] Discipline has random extraction
[ ] No DisciplineCheck UI exists
[ ] Diary Alerts and Price Alerts are visually distinct
[ ] Watchlist is stock-focused
[ ] Stock Note is current mutable view
[ ] Stock Timeline Record feels immutable
[ ] ETF research appears only under Tools / Market Rotation Monitor
[ ] Market Rotation Monitor does not dominate the product
[ ] Dark mode is fully polished
[ ] P/L colors are calm, not casino-like
[ ] Price/P&L movement does not rely only on color
[ ] All screens have loading / empty / error states
[ ] Mobile bottom nav works
[ ] Right context panel is contextual
[ ] Typography uses tabular numbers
[ ] No landing-page hero section inside app
```

---

## 24. Final Design Summary

The UI should feel like:

```text
A calm dark trade journal that connects daily market observation, trading actions, P/L outcomes, and later review.
```

The product should not make the user trade more.  
It should help the user think better, record better, and review better.
