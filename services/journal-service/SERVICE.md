# Journal Service

Owns `journal.*`: diaries, their transactions, and deterministic Quick Note creation/append.

- Never reads or writes another service schema.
- Every user-owned lookup includes both `user_id` and resource ID.
- Missing and cross-user resources both return 404.
- Transactions are diary records only; no holdings or cost basis is derived.
- `POST /internal/diaries`, `POST /internal/quick-note`, and transaction creation accept an optional `Idempotency-Key` (maximum 200 characters). The key is scoped by user and operation; an identical retry returns the stored result, while reuse with another payload returns 409.

## Partner diary projection

`GET /internal/partner-diaries?ownerId&from&to` is Edge-composition only (not a browser Edge route).

- Viewer JWT required; self-read as partner is `404`.
- Partner service is asked for diary authorization; deny → `404`, Partner unavailable → `503`.
- Response is a sanitized projection: `id`, `localDate`, `title`, `content`, `tags` only. Transactions, reviews, and internal metadata are never selected.
- Inclusive max range: `to.DayNumber - from.DayNumber <= 365` (inclusive 366-day window). Larger ranges return `invalid_date_range`. Diary-review summary uses the same rule via `DiaryReviewRules.InvalidRange`.
