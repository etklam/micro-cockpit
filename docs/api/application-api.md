# Browser-facing application API

The generated [`edge-api.openapi.json`](../../contracts/openapi/edge-api.openapi.json) is authoritative. Main families:

| Family | Purpose |
|---|---|
| `/api/auth/*` | register, login, refresh, logout |
| `/api/app/bootstrap`, `/api/app/settings` | application context and preferences |
| `/api/app/quick-observations`, `/api/app/market-observations*` | capture, history, deletion |
| `/api/app/observation-updates*` | enrichment, Expectations, Action Decisions |
| `/api/app/expectations*` | lifecycle and owner review |
| `/api/app/action-decisions*` | decisions and optional Trade evidence |
| `/api/app/watchlist*` | Instrument membership and notes |
| `/api/app/pattern-review`, `/api/app/discipline-principles*` | transparent self-review |
| `/api/app/comparison` | read-only, grant-scoped Human / Agent Observation and Expectation comparison |
| `/api/app/calendar` | Journal Day activity |
| `/api/app/market/*` | Instrument directory, bars, provider health |
| `/api/app/tools/*`, presets, saved calculations | calculators and snapshots |
| `/api/app/agents`, `/api/app/access-grants` | Agent User and grant management |
| `/api/agent/journal-records`, `/api/agent/journal-changes` | full and incremental granted reads |
| `/api/app/account-export`, `/api/app/account` | data portability and permanent deletion |

Mutations use typed JSON and stable ProblemDetails. Selected creates accept `Idempotency-Key`. Owner-scoped missing and cross-owner resources use non-disclosing failures.

Retired Diary, reminders, Performance, Partner, article, price-alert, rotation, stock-page, and operations routes are not mapped.
