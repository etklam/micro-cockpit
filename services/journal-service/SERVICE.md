# Journal Service

Owns `journal.*`: diaries, their transactions, and deterministic Quick Note creation/append.

- Never reads or writes another service schema.
- Every user-owned lookup includes both `user_id` and resource ID.
- Missing and cross-user resources both return 404.
- Transactions are diary records only; no holdings or cost basis is derived.
- `POST /internal/diaries`, `POST /internal/quick-note`, and transaction creation accept an optional `Idempotency-Key` (maximum 200 characters). The key is scoped by user and operation; an identical retry returns the stored result, while reuse with another payload returns 409.
