# DESIGN.md — Micro Cockpit Design System

> Calm. Precise. Restrained.
> A quiet instrument panel for post-session reflection — graphite and ink with one restrained signal lamp.

This document is the source of truth for visual and interaction design. Product boundaries live in [PRODUCT.md](./PRODUCT.md). Implementation tokens live in `frontend/src/index.css`.

---

## 1. Intent

Micro Cockpit is a **diary-first trade journal**. The UI exists so a solo trader, after the market closes, can:

1. Capture what they noticed (quick note / diary)
2. Attach the trades and the day’s P/L
3. Meet one personal discipline principle
4. Review later without shame or theater

It is **not** a brokerage, portfolio ledger, or terminal. Numbers serve reflection; reflection is never decoration for the numbers.

### Brand personality

| Word | Means in the UI |
| --- | --- |
| **Calm** | Low light, soft elevation, no flashing state |
| **Precise** | Tabular numbers, hairline structure, consistent spacing |
| **Restrained** | One chrome accent at a time, sparse chrome, copy that never congratulates or scolds |

### Anti-references

Do not drift toward:

- Consumer trading apps — confetti, neon gain/loss, streak pressure
- Navy-and-gold “premium fintech” costume
- 2026 warm-cream AI defaults
- Generic SaaS dashboards — flat blue sidebars, identical card grids, eyebrow-on-everything, hero-metric tiles
- Decorative motion that makes the trader wait

---

## 2. Design principles

1. **Reflection and numbers, equal weight.** Prose gets a real reading measure (serif, ~65–70ch). Numbers get tabular precision and signed direction.
2. **Quiet by default.** Surfaces are deep and calm. One chrome accent (red or green) does all brand work. Semantic gain/loss stay separate. Negative space is a material.
3. **Instrument, not stage.** Density only where the task needs it. Familiar affordances. Consistent vocabulary screen to screen.
4. **Honesty over performance.** Copy never gamifies. “You showed up today” is the ceiling of praise.
5. **Reward opening it.** The daily discipline principle and the quick-note capture sit at the top of the day. Starting to write is the lowest-friction act on screen.

---

## 3. Information architecture

### Desktop primary nav

```text
Today · Diary · Calendar · Discipline · Alerts
More ▸ Monthly review · Watchlist · Price alerts · Market rotation · Partners · Articles · Tools
```

### Mobile bottom nav

```text
Today · Diary · Calendar · Discipline · More
```

### Core routes

```text
/                         public landing (anonymous); redirects to /today when signed in
/tools                    public + authenticated (same calculators; shell differs)
/login  /register
/today
/diary  /diary/:diaryId
/calendar/:year/:month
/discipline
/alerts
/more
/review  /review/:year/:month
/watchlist  /price-alerts  /rotation
/partners  /articles  /articles/:slug
/settings
```

Quick Note is an **action on Today**, not a separate route.

### Public vs authenticated surfaces

| Surface | Auth | Shell |
| --- | --- | --- |
| `/` landing | Anonymous only (signed-in → `/today`) | `PublicShell` — brand, Tools, Sign in, Register, locale, theme toggle |
| `/tools` | **Both** | Anonymous → `PublicShell`; signed-in → app `Shell` (rail / bottom nav) |
| `/login`, `/register` | Anonymous | Standalone auth cards |
| Everything under the authenticated tree | Required | App `Shell` |

Tools are intentionally dual-access: no account required to calculate; diary and account data remain behind auth.

---

## 4. Layout system

### Desktop shell

```text
┌────────────┬────────────────────────────────────────┐
│  Rail      │  Main workspace                        │
│  240px     │  max-width 1080px, centered            │
│            │                                        │
│  Brand     │  Page header                           │
│  Primary   │  Content stack                         │
│  More      │                                        │
│  Sign out  │                                        │
└────────────┴────────────────────────────────────────┘
```

| Region | Width / rule |
| --- | --- |
| Sidebar rail | `240px` sticky, full viewport height |
| Content max | `1080px` |
| Page pad X | `clamp(16px, 4vw, 48px)` |
| Content gap | `24px` vertical rhythm |

The rail is graphite chrome — darker than the workspace, separated by a hairline, never a saturated brand slab.

### Mobile shell

```text
┌──────────────────────────┐
│ Compact header (sticky)  │
├──────────────────────────┤
│ Page content             │
├──────────────────────────┤
│ Bottom nav (safe-area)   │
└──────────────────────────┘
```

- No permanent sidebar below `920px`
- Prefer card lists over dense tables
- Primary capture actions stay one tap away
- Respect `env(safe-area-inset-*)`

### Composition patterns

| Pattern | When |
| --- | --- |
| **Writing desk** | Today, Diary editor — prose-first, large textarea, ambient instruments secondary |
| **Instrument strip** | Compact metrics in a horizontal or auto-fit grid (P/L, counts, status) |
| **Timeline** | Diary list, recent reflections |
| **Month grid** | Calendar (desktop); list-first on narrow viewports |
| **Research split** | Watchlist / stock context — list + detail columns |

Avoid stacking three equal “metric tiles” as the visual identity of a page. Prefer one primary surface + quieter instruments.

---

## 5. Color system

Two **independent axes** control chrome. All tokens are **OKLCH**. Source of truth: `frontend/src/index.css`.

| Axis | Values | DOM | Meaning |
| --- | --- | --- | --- |
| **Scheme** | `light` · `dark` | `html[data-theme]` | Surfaces, ink, borders, shadows |
| **Accent** | `red` · `green` | `html[data-accent]` (or equivalent) | Brand / chrome signal only (`--primary*`, `--ring`, focus, active nav) |

Dark remains the home scheme; light is a true-neutral day desk (not cream, not violet wash). Surfaces stay nearly neutral graphite/stone. Accent is a **single signal lamp**, never wallpaper.

### Four chrome presets

Resolved scheme × accent yields four presets. Settings may expose them as a grid or as separate scheme + accent controls — product of the two axes is what matters.

| Preset | Scheme | Accent |
| --- | --- | --- |
| Dark · green | `dark` | `green` |
| Dark · red | `dark` | `red` |
| Light · green | `light` | `green` |
| Light · red | `light` | `red` |

There is **no amber-iron brand requirement**. Amber/hue-72 is not a locked brand color; chrome accent is red or green only. Do not reintroduce violet/blue-purple or navy-gold costume as brand.

### Preference & persistence (incl. legacy `system`)

| Layer | Behavior |
| --- | --- |
| Preference | Account settings + local mirror (`td_appearance` today; extend or pair with accent key as needed) |
| Legacy `system` | Still valid: **scheme only** follows OS `prefers-color-scheme`. It is **not** a fifth accent. Accent defaults to a fixed product default (prefer `green`) when unset. |
| Explicit `light` / `dark` | Scheme fixed; accent independent |
| Toggle | One-tap light/dark flips **scheme** only (same behavior as today: drops `system` for an explicit scheme). Does not flip accent or swap gain/loss. |
| Boot | Apply mirrored preference before React paint (`bootAppearanceFromMirror`) to reduce flash |
| Logout | Keep last mirrored scheme/accent on public/login surfaces (no account leak — chrome preference only) |
| Server reconcile | Bootstrap / settings `appearance` wins when valid; invalid values ignored |

Implementation may keep API field `appearance: system \| light \| dark` and store accent separately (client-only or future settings field). Documented contract for UI: two axes, four presets, legacy `system` scheme-follow.

### Surfaces

| Token | Role |
| --- | --- |
| `--bg` | App canvas |
| `--bg-elevated` | Deepest well / rail underlay |
| `--sidebar` | Navigation rail |
| `--surface` | Cards, panels, raised work areas |
| `--surface-2` | Inputs, inset wells, nested chrome |
| `--overlay` | Hover / raised interaction |

### Text

| Token | Role |
| --- | --- |
| `--ink` | Primary body and titles |
| `--muted` | Secondary labels, meta |
| `--faint` | Tertiary, large decorative only |

### Brand — chrome accent tokens

Accent drives **only** brand chrome. Roles:

| Token | Role |
| --- | --- |
| `--primary` | Links, focus ink, active nav, indicators |
| `--primary-hover` / `--primary-press` | Interactive variants |
| `--primary-btn` / `--primary-btn-hover` | Filled primary actions |
| `--primary-soft` | Soft fills (active nav wash, selected day) |
| `--primary-line` | Focus borders, selected outlines |
| `--on-primary` | Label on filled primary |
| `--accent` | Alias of `--primary` where legacy class names need it |
| `--ring` | Focus-visible outline (derived from primary) |

**Changing accent tokens:** override the `--primary*` family (and derived `--ring` / `--accent`) under each `html[data-theme][data-accent]` (or nested selector) block in `frontend/src/index.css`. Do **not** retarget `--gain` / `--loss` when swapping accent. Keep soft mixes via `color-mix` so both red and green stay restrained (never neon).

### Semantic (independent of accent; restrained; never neon)

| Token | Role |
| --- | --- |
| `--gain` / `--gain-soft` | Positive P/L / constructive success |
| `--loss` / `--loss-soft` | Negative P/L, destructive |
| `--warn` / `--warn-soft` | Active reminders |
| `--success*` / `--danger*` | Aliases of gain/loss for component vocabulary |

**Independence rule:** chrome accent never redefines semantic direction. A **green accent** still uses `--loss` for negative P/L; a **red accent** still uses `--gain` for positive P/L. Primary buttons and nav may share a hue family with gain *or* loss depending on accent choice — that is chrome, not a rewrite of signed numbers.

**Direction is never color alone.** Always pair with a sign (`+` / `−`), arrow, or label (see `signed()` / `pct()` in `format.ts`).

### Lines

```text
--border         ~9% ink over transparent
--border-strong  ~16% ink
--hairline       ~5.5% ink
```

Prefer hairlines and soft elevation over heavy frames.

### Contrast floor

WCAG 2.2 AA minimum:

- Body ink → bg ≥ 4.5:1 (shipped ~16:1)
- Muted → bg ≥ 4.5:1 for essential text (≥ 7:1 shipped)
- Primary button label → button ≥ 4.5:1 for **both** red and green accents in **both** schemes
- Focus ring clearly visible on all interactive surfaces

---

## 6. Typography

### Families

| Role | Stack |
| --- | --- |
| UI / chrome | **Inter Variable** → system UI sans |
| Prose / diary / discipline quotes | **Newsreader Variable** → Georgia → serif |

### Scale

| Token | Size | Use |
| --- | --- | --- |
| `--fs-xs` | 12px | Captions, meta, badges |
| `--fs-sm` | 13px | Field labels, secondary |
| `--fs-base` | 15px | Body UI |
| `--fs-md` | 16px | Emphasized body / prose |
| `--fs-lg` | 18px | Section titles, quotes |
| `--fs-xl` | 21px | Card / entry titles |
| `--fs-2xl` | 26px | Page titles |
| `--fs-3xl` | 36px | Hero numbers (rare) |

### Rules

- Page titles: weight 600, tight tracking, `text-wrap: balance`
- Instrument labels: small, medium weight, muted — **not** shouty uppercase everywhere; reserve wide tracking for true chrome labels (e.g. “More”)
- Diary body: `.prose` — Newsreader, relaxed leading, max ~70ch
- All money, ranks, percents, calendar cells: `.num` → `font-variant-numeric: tabular-nums`
- Inter feature settings: `cv02 cv03 cv04 cv11 ss01` for cleaner UI glyphs

---

## 7. Spacing, radius, elevation

### Spacing (4px grid)

```text
4 · 8 · 12 · 16 · 20 · 24 · 32 · 40 · 56 · 72
```

Prefer multiples of 4. Page vertical rhythm uses `--sp-6` (24px) between major blocks.

### Radius

| Token | Value | Use |
| --- | --- | --- |
| `--r-xs` | 6px | Tight chips |
| `--r-sm` | 8px | Small controls |
| `--r-md` | 10px | Buttons, inputs, nav items |
| `--r-lg` | 14px | Cards |
| `--r-xl` | 18px | Dialogs, login |
| `--r-pill` | 999px | Badges, focus marks |

### Elevation (dark: soft, wide, low)

| Token | Use |
| --- | --- |
| `--shadow-sm` | Subtle rest |
| `--shadow-md` | Raised panels |
| `--shadow-lg` | Modal / login |
| `--ring` | Focus-visible only |

Cards primarily use **surface + hairline border**, not heavy drop shadows. Shadow is for true overlays.

---

## 8. Motion

| Token | Value | Use |
| --- | --- | --- |
| `--dur-fast` | 140ms | Hover, color |
| `--dur` | 200ms | Standard UI |
| `--dur-slow` | 320ms | Modal enter |
| `--ease-out` | `cubic-bezier(0.22, 1, 0.36, 1)` | Entrances |
| `--ease-standard` | `cubic-bezier(0.4, 0, 0.2, 1)` | Utility |

Rules:

- Transition **color / background / border / opacity / shadow** — not layout thrash
- No orchestrated page-load choreography
- Skeleton shimmer is the only continuous motion in resting UI
- `prefers-reduced-motion: reduce` collapses all motion to near-instant / static

---

## 9. Component vocabulary

Shared primitives live in `frontend/src/ui.tsx` + `App.css`. No component library dependency.

### Core

| Component | Notes |
| --- | --- |
| `Button` | `primary` · `subtle` · `ghost` · `danger` · sizes `sm`/`md` · loading spinner |
| `IconButton` | 36×36 hit target, `aria-label` required |
| `Card` | Surface + hairline; `flush` for nested layouts |
| `Field` | Label + control + hint/error |
| `TextInput` / `TextArea` / `SelectBox` | Shared `.input` chrome |
| `Badge` | `muted` · `primary` · `gain` · `loss` · `warn` |
| `Stat` | Compact instrument metric |
| `PageHeader` | Title + optional subtitle + actions |
| `EmptyBox` / `ErrorBox` | Honest empty and failure states |
| `Skeleton` / `SkeletonText` | Loading placeholders |
| `Brand` / `Gauge` | Wordmark + gauge mark |
| `useConfirm` | Accessible alertdialog |

### Shell

| Piece | Notes |
| --- | --- |
| Sidebar rail | Brand, primary nav, More group, sign out |
| Mobile top | Compact brand + sign out |
| Mobile bottom nav | Five slots; More aggregates secondary |
| Content column | Max-width, page pad, vertical stack |

### Interaction states

Every interactive control supports:

- rest · hover · active/press · focus-visible · disabled · loading (where async)

Focus-visible uses `--ring` (accent soft outline). Never remove focus without a visible replacement.

---

## 10. Page patterns

### Login / register

Centered card on deep canvas. Soft radial signal glow from the **current accent** — atmospheric, not decorative overload. Copy: plain, adult. No marketing hero. Foot links back to landing and tools.

### Landing (public `/`)

Anonymous marketing-light home. Not a dashboard. Structure (see `LandingPage.tsx` + `landing.*` i18n keys):

1. **Hero** — eyebrow, title, lead, CTAs: Create account · Sign in · Try tools free
2. **What it is for** — short body + four feature cards (diary, P/L calendar, discipline, monthly review)
3. **Tools without signing in** — catalogue teaser grid linking to `/tools?tool=<id>` + “Open tools”
4. **Foot CTA** — private-record invite + Create account · Sign in

Chrome: `PublicShell` sticky top (brand · Tools · Sign in · Register · locale · theme toggle). No app rail, no diary chrome, no confetti.

### Today — writing desk

Primary question: *What should I capture before it fades?*

Order:

1. Greeting + long date (quiet)
2. Pending reminder banner (only if count > 0)
3. **Quick note** — full-width prose surface (primary)
4. **Instrument strip** — diary status · daily P/L · today’s discipline (secondary)
5. Recent reflections list

Discipline quote uses serif italic. P/L always signed. Diary status prefers “You showed up today.” over scores.

### Diary

- Editor card above the list (low friction to write)
- Timeline of entries: date meta, title, prose body
- Trades live on the detail surface, not as portfolio accounting
- Structured decision review is progressive disclosure (`details`)

### Calendar

- Month grid on desktop; horizontal scroll acceptable on narrow
- Day cell: number · compact signed P/L · diary presence dot
- Selected day opens P/L editor below/aside — reflection support, not a game board
- Profit/loss tints are soft washes; never pure neon fills

### Discipline

Serif principle lines. Add form is inline and quiet. Random/today principle is a gift, not a streak counter.

### Alerts / research

List density without terminal noise. Filters as plain fields. Tables use sticky headers, tabular numbers, restrained MA/status chips.

### Tools (`/tools`)

Shared calculator surface for public and authenticated users. Client-side pure math in `frontend/src/features/toolsCalc.ts` (same formulas as tool-service). Query `?tool=<ToolId>` selects the calculator; default `position-sizing`.

#### Canonical tool catalogue

Single ordered list of public calculators. Landing teaser, tools select, and `TOOL_IDS` must stay aligned.

| `ToolId` | Purpose |
| --- | --- |
| `position-sizing` | Risk amount + share quantity from account, risk %, entry, stop |
| `risk-reward` | Distance to stop/target + ratio |
| `fire` | Nest-egg target + gap from expenses and withdrawal rate |
| `relative-value` | Current vs historical ratio, deviation % |
| `seasonality` | Average return + win rate from period returns |

All five are **public** (no auth). There is no separate “auth-only tools” catalogue in v1; authenticated users get the same tools inside the app shell (and full diary elsewhere).

#### Adding a tool

1. Add `ToolId` + pure `calculateTool` branch in `toolsCalc.ts` (and unit coverage in `toolsCalc.test.ts`).
2. Align Edge/tool-service contract if a server twin exists; regenerate OpenAPI client only when the public API changes.
3. Extend `ToolsPage` field map + select option labels (i18n when strings are keyed).
4. Extend landing `TOOLS` teaser + `landing.tool.*` message keys (`en` + `zh-Hant`).
5. Keep default selection and `?tool=` validation on `isToolId`.
6. Do not put account-bound diary data into public tools.

### Monthly review

Two-column process + outcome on desktop; stack on mobile. Charts are thin instrument bars, not celebration graphics.

---

## 11. Iconography

Hand-rolled 24×24 set in `icons.tsx`:

- Stroke 1.6, round caps/joins, `currentColor`
- No external icon pack
- Gauge mark is the brand glyph (login, rail, favicon)

Keep new icons in the same optical weight. Prefer recognition over decoration.

---

## 12. Content voice

| Do | Don’t |
| --- | --- |
| “What did you notice today?” | “Crush your goals 🚀” |
| “You showed up today.” | “🔥 12-day streak!” |
| “No entry yet today.” | “You haven’t journaled — you’re falling behind.” |
| “Couldn’t reach the cockpit.” | Generic “Error 500” without recovery |
| “Save note” | “Submit” on reflective capture |

Empty states explain what will appear and how to begin — never blame.

---

## 13. Accessibility

- WCAG 2.2 AA floor
- Interactive targets ≥ 24×24 CSS px (mobile nav ≥ 48px tall)
- Full keyboard paths; no hover-only actions
- Confirm dialogs trap focus, Esc cancels, labelled by title
- `prefers-reduced-motion` honored
- P/L direction not color-alone
- Money labeled with currency when known
- Semantic landmarks: `aside` nav, `main#content`, labelled nav regions

---

## 14. Implementation map

| Concern | Location |
| --- | --- |
| Tokens + base (scheme × accent) | `frontend/src/index.css` |
| Components + layout + pages | `frontend/src/App.css` |
| Primitives | `frontend/src/ui.tsx` |
| Icons / brand mark | `frontend/src/icons.tsx`, `ui.tsx` `Gauge` |
| Shell, public shell, routes | `frontend/src/App.tsx` |
| Landing | `frontend/src/LandingPage.tsx` |
| Appearance preference / mirror | `frontend/src/features/appearance.ts`, `useAppearance.ts` |
| Tool catalogue + pure calc | `frontend/src/features/toolsCalc.ts`, `latePages.tsx` `ToolsPage` |
| Page surfaces | `pages.tsx`, `latePages.tsx`, `MonthlyReviewPage.tsx`, `screens/*` |
| Formatting helpers | `frontend/src/format.ts` |
| i18n (landing, settings, nav) | `frontend/src/i18n/messages/{en,zh-Hant}.ts` |
| Fonts | Inter Variable + Newsreader Variable (`main.tsx`) |

### Engineering constraints

- Plain CSS classes — no CSS-in-JS, no Tailwind, no component library
- Scheme via `html[data-theme="dark|light"]` + matching `color-scheme`; accent via a second root attribute (e.g. `data-accent="red|green"`)
- Semantic gain/loss tokens never keyed off accent selection
- Frontend talks only to Edge API (tools may run fully client-side)
- Preserve existing class names used by tests and page markup unless intentionally migrated together

### Changing accent tokens (checklist)

1. Edit `--primary*` (and derived soft/line/ring) for each scheme × accent block in `index.css`.
2. Verify primary button contrast AA in all four presets.
3. Spot-check focus ring, active nav wash, login glow, links.
4. Confirm P/L badges and calendar washes still use `--gain` / `--loss` only.
5. Update this document only if roles or axes change — not for routine hue tweaks.

---

## 15. Quality bar checklist

Before shipping UI changes:

- [ ] Feels calm in low light for 30+ minutes of reading/writing
- [ ] One chrome accent at a time; surfaces stay graphite/stone
- [ ] Red and green accents both pass primary-button and focus contrast
- [ ] Gain/loss semantics unchanged when accent changes
- [ ] Legacy `system` still follows OS scheme without inventing a fifth accent
- [ ] Landing structure and tool catalogue stay aligned with `toolsCalc` / i18n
- [ ] Diary prose is actually pleasant to read
- [ ] Numbers are tabular and signed
- [ ] Empty / error / loading states are honest and recoverable
- [ ] Keyboard + focus-visible work end to end
- [ ] Mobile safe areas and bottom nav don’t obscure primary actions
- [ ] No confetti, streaks-as-pressure, or casino color
- [ ] Diff does not reintroduce generic SaaS “metric tile wallpaper” or amber-as-mandatory-brand

---

*This system should make the tool disappear into the trader’s evening ritual — precise when counting, quiet when writing.*
