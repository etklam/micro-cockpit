# Browser-facing application API

The browser calls Edge only. Paths below are Edge routes; Edge maps them to private `/internal/*` service routes. Generated request and response types in `frontend/src/generated/edge.ts` are the shape source of truth. This document explains intent, ownership, and failure behavior.

Unless marked public, routes require an Identity-issued bearer access token. Authenticated user IDs come from the token and are never accepted as ownership input.

## Common behavior

- JSON requests use camelCase.
- Errors use RFC 7807 ProblemDetails with stable codes where the UI needs translation.
- Missing and cross-user records generally both return `404`.
- Validation errors return `400`; duplicate or idempotency conflicts return `409`.
- Edge returns `502` for malformed typed downstream responses, `503` for unavailable services, and `504` for downstream timeouts.
- Mutating diary and calculation routes use `Idempotency-Key` where noted.

## Authentication and account

| Method and path | Purpose | Request | Response | Auth and errors |
|---|---|---|---|---|
| `POST /api/auth/register` | Create a human account | email, displayName, password, timezone, baseCurrency | created user | Public only when enabled; otherwise registration key required. `400`, `403`, `409`, `429`. |
| `POST /api/auth/login` | Start a browser session | email, password | accessToken, expiresAt | Public, rate limited. Edge stores refresh token as HttpOnly cookie. `401`, `429`. |
| `POST /api/auth/refresh` | Rotate refresh token and restore access | no JSON; cookie only | accessToken, expiresAt | Public route with cookie. Clears invalid cookie on failure. `401`, `429`. |
| `POST /api/auth/logout` | Revoke refresh family best-effort and clear browser cookie | none | `204` | Public and idempotent from browser perspective. |
| `GET /api/app/bootstrap` | Load user, timezone, currency, locale, appearance, role, product areas, local date | none | `AppBootstrapResponse` | Authenticated. Identity is required. |
| `GET /api/app/settings` | Load account preferences | none | timezone, baseCurrency, appearance, locale, accentTheme | Authenticated. |
| `PUT /api/app/settings` | Update account preferences | same preference fields | updated settings | Authenticated; validates IANA timezone, ISO currency, supported locale/theme. Session refresh follows in frontend. |

## Diaries, transactions, and reviews

| Method and path | Purpose | Request/response summary | Notes |
|---|---|---|---|
| `GET /api/app/diaries` | List owned diaries | filters/cursor in query; returns page of diary summaries | Authenticated with `diaryAccess`. |
| `POST /api/app/diaries` | Create a diary | localDate, title, Markdown content, tags; returns diary | Optional `Idempotency-Key`, max 200 chars. |
| `GET /api/app/diaries/{id}` | Read one diary | returns diary, tags, review state | Ownership-scoped. |
| `PUT /api/app/diaries/{id}` | Update diary content/date/tags | diary write; returns updated diary | Ownership-scoped. |
| `DELETE /api/app/diaries/{id}` | Delete diary | `204` | Publishes deletion event through outbox. |
| `POST /api/app/quick-note` | Create or append today's diary note | note text and account date context; returns diary | Deterministic, supports idempotency. |
| `GET /api/app/diaries/{diaryId}/transactions` | List diary transactions | returns transaction array | Diary ownership is checked first. |
| `POST /api/app/diaries/{diaryId}/transactions` | Add transaction | symbol, side, quantity, price, currency, notes | Optional idempotency key; no automatic trade execution. |
| `GET /api/app/diaries/{diaryId}/transactions/{id}` | Read one transaction | transaction DTO | Used by Tool service source validation as well as browser access. |
| `PUT /api/app/diaries/{diaryId}/transactions/{id}` | Edit transaction | transaction write | Both diary and transaction owner are checked. |
| `DELETE /api/app/diaries/{diaryId}/transactions/{id}` | Delete transaction | `204` | Ownership-scoped. |
| `GET/PUT/DELETE /api/app/diaries/{diaryId}/review` | Read, save, or remove structured review | review scores/text/tags; review DTO or `204` | One review per diary and user. |
| `GET /api/app/diary-review-summary` | Aggregate review data for date range | `from`, `to` query | Inclusive range cannot exceed 366 days. |
| `GET /api/app/diary-review-items` | List filterable review items | range, status, assessment, tag, cursor, limit | Paginated review workspace. |

## Tools

Calculator endpoints require authentication but mirror the public client formulas. Anonymous browser calculations do not call them.

| Method and path | Purpose | Validation and response |
|---|---|---|
| `POST /api/app/tools/position-sizing` | Calculate quantity and planned risk | Positive account/entry/stop, risk 0 to 100, entry differs from stop. Returns quantity, plannedLoss, riskBudget, positionValue, perUnitRisk. |
| `POST /api/app/tools/risk-reward` | Calculate reward/risk | Positive prices with a valid long or short ordering. Returns ratio, riskPerUnit, rewardPerUnit, breakevenWinRate. |
| `POST /api/app/tools/average-cost` | Calculate combined average | Four positive quantity/cost inputs. Returns averageCost, totalQuantity, totalCost, averageCostChange. |
| `POST /api/app/tools/profit-loss` | Calculate long/short P/L | Valid side, positive prices/quantity, nonnegative fees. Returns netPnl, returnPercent, grossPnl, totalFees, exitValue. |
| `GET /api/app/tool-presets` | List owned presets | Returns newest-used/updated first. |
| `POST /api/app/tool-presets` | Create named partial assumptions | Name max 80, strict tool type and allowed keys, optional ISO currency. Case-insensitive duplicate name returns `409`. |
| `PUT /api/app/tool-presets/{id}` | Replace owned preset | Same validation as create. `404` for missing/cross-user. |
| `POST /api/app/tool-presets/{id}/use` | Set `lastUsedAt` | No calculation is submitted. |
| `DELETE /api/app/tool-presets/{id}` | Delete owned preset | `204` or `404`. |
| `GET /api/app/saved-calculations?limit=10` | List recent owned snapshots | Limit 1 to 50; newest first. |
| `POST /api/app/saved-calculations` | Explicitly save calculation | Requires idempotency key length 8 to 100. Backend validates inputs and recalculates output. Optional source is checked through Journal. |
| `DELETE /api/app/saved-calculations/{id}` | Delete owned snapshot | `204` or non-disclosing `404`. |

Saved-calculation request fields are `toolType`, `inputs`, `currency`, optional `symbol`, optional `sourceDiaryId`, optional `sourceTransactionId`, and optional `note`. `sourceTransactionId` requires `sourceDiaryId`. The request does not contain output.

## Screen composition

| Method and path | Purpose | Downstream services |
|---|---|---|
| `GET /api/app/dashboard` | Today screen | Identity context, Journal, Performance, Discipline, Reminder/Price Alert capabilities |
| `GET /api/app/calendar?year&month` | Monthly diary/performance calendar | Journal and Performance, plus alert capability where represented |
| `GET /api/app/stocks/{symbol}/page` | Stock research page | Stock Research and Market Data |
| `GET /api/app/rotation/monitor` | Rotation monitor DTO | Rotation service with Edge validation/shape |
| `GET /api/app/partners/{linkId}/compare` | Shared partner comparison | Partner summary/policy and sanitized Journal projection |

Composition treats required failures as whole-request errors and optional failures as explicit unavailable capability states. It does not convert failures to empty arrays.

## Research, market, alerts, and rotation

| Route family | Operations | Owner |
|---|---|---|
| `/api/app/stocks`, `/watchlist` | Stock directory, watchlist add/remove | Stock Research |
| `/api/app/stocks/{id}/note` | Current mutable stock note | Stock Research |
| `/api/app/stocks/{id}/timeline`, `/timeline/*` | Append evidence and corrections | Stock Research |
| `/api/app/market/symbols`, `/bars/{symbol}`, `/providers/health` | Published symbol/bar/provider reads | Market Data |
| `/api/app/price-alerts*` | Alert CRUD, dismiss/reactivate, trigger history | Price Alert |
| `/api/app/rotation/universes*` | Universe CRUD, symbols, manual calculate | Rotation |

Price Alert creation validates rule-specific fields. Activation fails closed when provider health is stale. Rotation calculation stores data under its formula version and reports insufficient lookback as data status, not zero.

## Partners, content, and administration

| Route family | Purpose | Auth |
|---|---|---|
| `/api/app/partners/invitations*` | Create/list/revoke/redeem single-use human invitation codes | Authenticated; create and redeem are rate limited |
| `/api/app/partners/{id}` | Revoke link, accept legacy pending link, update share policy | Link member only |
| `/api/app/partners/{id}/summary` | Resolve partner-facing summary | Accepted member only |
| `/api/content/posts*` | List/read published educational content | Public |
| `/api/admin/posts*` | Create/update/delete content | Admin |
| `/api/admin/operations/*` | Read audit/jobs and record health/job requests | Admin |

Invitation codes are returned only at creation and stored as hashes. Partner compare uses non-disclosing access rules and never returns transactions or reviews.

## Source contracts

- Committed service and Edge documents: `contracts/openapi/*.openapi.json`
- Generated browser client: `frontend/src/generated/edge.ts`
- API conventions and null semantics: [reference-api-data.md](../reference-api-data.md)
- Authorization policy matrix: [authorization-matrix.md](../authorization-matrix.md)
