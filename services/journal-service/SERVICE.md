# Journal Service

Owns `journal.*`: diaries, their transactions, and deterministic Quick Note creation/append.

- Never reads or writes another service schema.
- Every user-owned lookup includes both `user_id` and resource ID.
- Missing and cross-user resources both return 404.
- Transactions are diary records only; no holdings or cost basis is derived.
