# Authorization matrix

This matrix is the Phase 1 runtime contract. `—` means no Edge route, role, scope, or service key is applicable. A human JWT includes administrators unless a row explicitly requires the `admin` role. The default rule is: an agent is denied unless the row names an explicit scope.

Cross-user resources remain concealed with `404`; authentication failures use the endpoint's existing `401`/`403` convention. Service-key endpoints do not trust Bearer identity and compare `X-Service-Key` in constant time.

## Edge-only and authentication routes

| service | method | path | public Edge path | allowed principal type | required role | required scope | required service key | cross-user behavior |
|---|---|---|---|---|---|---|---|---|
| Edge | GET | `/health/live` | same | anonymous | — | — | — | n/a |
| Edge | GET | `/health/ready` | same | anonymous | — | — | — | n/a |
| Edge | GET | `/version` | same | anonymous | — | — | — | n/a |
| Identity | POST | `/internal/auth/register` | `/api/auth/register` | anonymous | — | — | public when enabled; otherwise registration key; Edge `auth-register` rate limit | n/a |
| Identity | POST | `/internal/auth/login` | `/api/auth/login` | anonymous | — | — | Edge `auth-login` rate limit | invalid credentials are not resource-disclosing |
| Identity | POST | `/internal/auth/refresh` | `/api/auth/refresh` | anonymous with refresh token | — | — | Edge `auth-refresh` rate limit | token family ownership enforced |
| Identity | POST | `/internal/auth/logout` | `/api/auth/logout` | anonymous with refresh token | — | — | — | token family ownership enforced |
| Identity | POST | `/internal/auth/api-key/token` | `/api/auth/api-key/token` | anonymous with API key | — | issued key scopes; Edge `auth-login` rate limit | — | key resolves only its agent |
| Identity | POST | `/internal/auth/agents` | `/api/app/agents` | human user, admin | — | — | — | created agent belongs to caller |
| Identity | DELETE | `/internal/auth/api-keys/{id}` | `/api/app/api-keys/{id}` | human user, admin | — | — | — | another creator's key is `404` |
| Identity | GET | `/internal/auth/me` | — | human user, admin | — | — | — | another user cannot be selected |
| Identity | GET, PUT | `/internal/auth/settings` | `/api/app/settings` | human user, admin | — | — | — | caller row only; agents concealed/denied |
| Identity | GET | `/internal/auth/sso/providers` | — | anonymous | — | — | — | n/a |
| Edge aggregation | GET | downstream read set | `/api/app/dashboard` | human user, admin | — | — | — | every downstream query uses caller ownership |
| Edge aggregation | GET | downstream read set | `/api/app/calendar` | human user, admin | — | — | — | every downstream query uses caller ownership |
| Edge aggregation | GET | stock + market reads | `/api/app/stocks/{symbol}/page` | human user, admin; agent | — | `research:read` for agent | — | owned stock data remains `404` |

## Journal and user-owned supporting services

| service | method | path | public Edge path | allowed principal type | required role | required scope | required service key | cross-user behavior |
|---|---|---|---|---|---|---|---|---|
| Journal | GET | `/internal/diaries` | `/api/app/diaries` | human user, admin; agent | — | `diary:read` for agent | — | caller rows only |
| Journal | GET | `/internal/diaries/{id}` | — | human user, admin; agent | — | `diary:read` for agent | — | `404` |
| Journal | GET | `/internal/diary-day-summary` | dashboard/calendar downstream | human user, admin; agent | — | `diary:read` for agent | — | caller rows only |
| Journal | GET | `/internal/diaries/{diaryId}/transactions` | `/api/app/diaries/{diaryId}/transactions` | human user, admin; agent | — | `diary:read` for agent | — | `404` |
| Journal | POST | `/internal/diaries` | `/api/app/diaries` | human user, admin; agent | — | `diary:write` for agent | — | created for caller |
| Journal | POST | `/internal/quick-note` | `/api/app/quick-note` | human user, admin; agent | — | `diary:write` for agent | — | target owned by caller or `404` |
| Journal | POST | `/internal/diaries/{diaryId}/transactions` | `/api/app/diaries/{diaryId}/transactions` | human user, admin; agent | — | `diary:write` for agent | — | `404` |
| Journal | PUT, DELETE | `/internal/diaries/{id}` | `/api/app/diaries/{id}` | human user, admin; agent | — | `diary:write` for agent | — | `404` |
| Journal | PUT, DELETE | `/internal/diaries/{diaryId}/transactions/{id}` | `/api/app/diaries/{diaryId}/transactions/{id}` | human user, admin; agent | — | `diary:write` for agent | — | `404` |
| Performance | PUT | `/internal/daily-performances/{date}` | `/api/app/daily-performance/{date}` | human user, admin | — | — | — | caller row only |
| Performance | DELETE | `/internal/daily-performances/{date}` | — | human user, admin | — | — | — | caller row only |
| Performance | GET | `/internal/performance/day/{date}` | dashboard/calendar downstream | human user, admin | — | — | — | caller row only |
| Performance | GET | `/internal/daily-performances` | calendar downstream | human user, admin | — | — | — | caller rows only |
| Performance | GET | `/internal/performance/month-summary` | dashboard/calendar downstream | human user, admin | — | — | — | caller rows only |
| Discipline | GET | `/internal/disciplines` | `/api/app/disciplines` | human user, admin | — | — | — | caller rows only |
| Discipline | POST | `/internal/disciplines` | `/api/app/disciplines` | human user, admin | — | — | — | created for caller |
| Discipline | PUT, DELETE | `/internal/disciplines/{id}` | `/api/app/disciplines/{id}` | human user, admin | — | — | — | `404` |
| Discipline | POST | `/internal/disciplines/reorder` | — | human user, admin | — | — | — | caller rows only |
| Discipline | GET | `/internal/disciplines/today` | dashboard downstream | human user, admin | — | — | — | caller rows only |
| Discipline | GET | `/internal/disciplines/random` | — | human user, admin | — | — | — | caller rows only |
| Reminder | GET | `/internal/diary-alerts` | `/api/app/diary-alerts` | human user, admin | — | — | — | caller rows only |
| Reminder | POST | `/internal/diary-alerts` | `/api/app/diary-alerts` | human user, admin | — | — | — | diary ownership checked; `404` |
| Reminder | PUT | `/internal/diary-alerts/{id}` | — | human user, admin | — | — | — | `404` |
| Reminder | DELETE | `/internal/diary-alerts/{id}` | `/api/app/diary-alerts/{id}` | human user, admin | — | — | — | `404` |
| Reminder | POST | `/internal/diary-alerts/{id}/dismiss` | `/api/app/diary-alerts/{id}/dismiss` | human user, admin | — | — | — | `404` |
| Reminder | GET | `/internal/diary-alerts/day-summary` | dashboard downstream | human user, admin | — | — | — | caller rows only |
| Reminder | GET | `/internal/diary-alerts/day-summaries` | calendar downstream | human user, admin | — | — | — | caller rows only |
| Price Alert | GET | `/internal/price-alerts` | `/api/app/price-alerts` | human user, admin | — | — | — | caller rows only |
| Price Alert | POST | `/internal/price-alerts` | `/api/app/price-alerts` | human user, admin | — | — | — | created for caller |
| Price Alert | PUT, DELETE | `/internal/price-alerts/{id}` | `/api/app/price-alerts/{id}` | human user, admin | — | — | — | `404` |
| Price Alert | POST | `/internal/price-alerts/{id}/dismiss` | `/api/app/price-alerts/{id}/dismiss` | human user, admin | — | — | — | `404` |
| Price Alert | POST | `/internal/price-alerts/{id}/reactivate` | `/api/app/price-alerts/{id}/reactivate` | human user, admin | — | — | — | `404` |
| Price Alert | GET | `/internal/price-alerts/{id}/triggers` | `/api/app/price-alerts/{id}/triggers` | human user, admin | — | — | — | `404` |
| Partner | GET, POST | `/internal/partners` | `/api/app/partners` | human user, admin | — | — | — | caller relationships only |
| Partner | POST | `/internal/partners/{id}/accept` | `/api/app/partners/{id}/accept` | human user, admin | — | — | — | `404` |
| Partner | DELETE | `/internal/partners/{id}` | `/api/app/partners/{id}` | human user, admin | — | — | — | `404` |
| Partner | PUT | `/internal/partners/{id}/share-policy` | `/api/app/partners/{id}/share-policy` | human user, admin | — | — | — | `404` |
| Partner | GET | `/internal/partners/{ownerId}/authorization` | `/api/app/partners/{ownerId}/authorization` | human user, admin | — | — | — | unauthorized relationship is concealed |

## Stock research, market, rotation, and tools

| service | method | path | public Edge path | allowed principal type | required role | required scope | required service key | cross-user behavior |
|---|---|---|---|---|---|---|---|---|
| Stock Research | GET | `/internal/stocks` | `/api/app/stocks` | human user, admin; agent | — | `research:read` for agent | — | shared catalog |
| Stock Research | GET | `/internal/stocks/{symbol}` | `/api/app/stocks/{symbol}` | human user, admin; agent | — | `research:read` for agent | — | shared catalog |
| Stock Research | GET | `/internal/watchlist` | `/api/app/watchlist` | human user, admin; agent | — | `research:read` for agent | — | caller rows only |
| Stock Research | GET | `/internal/stocks/{stockId}/note` | `/api/app/stocks/{stockId}/note` | human user, admin; agent | — | `research:read` for agent | — | `404`/empty only for caller |
| Stock Research | GET | `/internal/stocks/{stockId}/timeline` | `/api/app/stocks/{stockId}/timeline` | human user, admin; agent | — | `research:read` for agent | — | caller rows only |
| Stock Research | GET | `/internal/timeline/{id}` | `/api/app/timeline/{id}` | human user, admin; agent | — | `research:read` for agent | — | `404` |
| Stock Research | POST | `/internal/stocks` | `/api/app/stocks` | human user, admin | — | no agent write scope exists | — | shared catalog write; agent `403` |
| Stock Research | POST, DELETE | `/internal/watchlist/{stockId}` | `/api/app/watchlist/{stockId}` | human user, admin | — | no agent write scope exists | — | caller rows; agent `403` |
| Stock Research | PUT | `/internal/stocks/{stockId}/note` | `/api/app/stocks/{stockId}/note` | human user, admin | — | no agent write scope exists | — | caller row/`404`; agent `403` |
| Stock Research | POST | `/internal/stocks/{stockId}/timeline` | `/api/app/stocks/{stockId}/timeline` | human user, admin | — | no agent write scope exists | — | created for caller; agent `403` |
| Stock Research | POST | `/internal/timeline/{originalId}/corrections` | `/api/app/timeline/{originalId}/corrections` | human user, admin | — | no agent write scope exists | — | original owned or `404`; agent `403` |
| Market Data | GET | `/internal/v1/symbols` | `/api/app/market/symbols` | anonymous direct; Edge human user, admin | — | — | — | shared published data; agents denied at Edge |
| Market Data | GET | `/internal/v1/bars/{raw}` | `/api/app/market/bars/{symbol}` | anonymous direct; Edge human user, admin | — | — | — | shared published data; agents denied at Edge |
| Market Data | GET | `/internal/v1/providers/health` | `/api/app/market/providers/health` | anonymous direct; Edge human user, admin | — | — | — | shared published data; agents denied at Edge |
| Market Data | PUT | `/internal/admin/symbols/{raw}` | — | internal service | — | — | required | n/a |
| Market Data | POST | `/internal/admin/provider-runs` | — | internal service | — | — | required | n/a |
| Market Data | PUT | `/internal/admin/provider-runs/{id}/bars` | — | internal service | — | — | required | unknown run is `404` |
| Market Data | POST | `/internal/admin/provider-runs/{id}/complete` | — | internal service | — | — | required | unknown run is `404` |
| Rotation | GET, POST | `/internal/rotation/universes` | `/api/app/rotation/universes` | human user, admin | — | — | — | shared human feature; agents denied |
| Rotation | PUT, DELETE | `/internal/rotation/universes/{id}` | `/api/app/rotation/universes/{id}` | human user, admin | — | — | — | unknown resource `404` |
| Rotation | PUT | `/internal/rotation/universes/{id}/symbols` | `/api/app/rotation/universes/{id}/symbols` | human user, admin | — | — | — | unknown resource `404` |
| Rotation | POST | `/internal/rotation/universes/{id}/calculate` | `/api/app/rotation/universes/{id}/calculate` | human user, admin | — | — | — | unknown resource `404` |
| Rotation | GET | `/internal/rotation/monitor` | `/api/app/rotation/monitor` | human user, admin | — | — | — | unknown resource `404` |
| Tool | POST | `/internal/tools/position-sizing` | `/api/app/tools/position-sizing` | human user, admin | — | — | — | n/a; agents denied |
| Tool | POST | `/internal/tools/risk-reward` | `/api/app/tools/risk-reward` | human user, admin | — | — | — | n/a; agents denied |
| Tool | POST | `/internal/tools/fire` | `/api/app/tools/fire` | human user, admin | — | — | — | n/a; agents denied |
| Tool | POST | `/internal/tools/relative-value` | `/api/app/tools/relative-value` | human user, admin | — | — | — | n/a; agents denied |
| Tool | POST | `/internal/tools/seasonality` | `/api/app/tools/seasonality` | human user, admin | — | — | — | n/a; agents denied |

## Internal events, workers, content administration, and operations

| service | method | path | public Edge path | allowed principal type | required role | required scope | required service key | cross-user behavior |
|---|---|---|---|---|---|---|---|---|
| Reminder | POST | `/internal/events/diary-deleted` | — | internal service | — | — | required | payload user is accepted only from trusted publisher |
| Reminder | POST | `/internal/worker/run` | — | internal service | — | — | required | n/a |
| Price Alert | POST | `/internal/worker/run` | — | internal service | — | — | required | n/a |
| Rotation | — | no `/internal/worker/*` route exists | — | — | — | — | — | hosted worker is not HTTP-addressable |
| Content | GET | `/internal/posts` | `/api/content/posts` | anonymous | — | — | — | published content only |
| Content | GET | `/internal/posts/{slug}` | `/api/content/posts/{slug}` | anonymous | — | — | — | missing post is `404` |
| Content | POST | `/internal/admin/posts` | `/api/admin/posts` | admin | `admin` | — | — | author is caller |
| Content | PUT, DELETE | `/internal/admin/posts/{id}` | `/api/admin/posts/{id}` | admin | `admin` | — | — | another author's post is `404` |
| Operations | GET | `/internal/operations/audit` | `/api/admin/operations/audit` | admin | `admin` | — | — | non-admin concealed at service; blocked at Edge |
| Operations | POST | `/internal/operations/audit` | — | internal service | — | — | required | actor is server-controlled (`NULL` when absent), never caller-supplied |
| Operations | GET | `/internal/operations/jobs` | `/api/admin/operations/jobs` | admin | `admin` | — | — | non-admin concealed at service; blocked at Edge |
| Operations | POST | `/internal/operations/jobs` | `/api/admin/operations/jobs` | admin | `admin` | — | — | requester derived from admin JWT |
| Operations | POST | `/internal/operations/health` | `/api/admin/operations/health` | admin | `admin` | — | — | non-admin concealed at service; blocked at Edge |

No service currently exposes any other `/internal/events/*` or `/internal/worker/*` endpoint. No Edge route forwards a service key or exposes an internal worker/event/audit-write operation to browsers.
