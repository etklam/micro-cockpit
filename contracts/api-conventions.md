# API conventions

- Browser traffic terminates at the Edge API. Internal service URLs are never shipped to the frontend.
- Public Edge routes use `/api/v1/*`; service routes use `/internal/*`. Breaking changes require a new version.
- Authenticate with `Authorization: Bearer <JWT>`. Ownership comes from verified `sub`, never a caller-supplied user ID.
- Forward or create `X-Correlation-Id`; return it on responses and include it in structured logs.
- JSON uses camelCase and UTC RFC 3339 timestamps. Local calendar dates use `YYYY-MM-DD` with an explicit IANA timezone where conversion matters.
- Errors use `{ "error": "stable_code", "message": "safe detail" }`. Invalid input is 400, unauthenticated is 401, forbidden is 403, missing and cross-owner resources are both 404, conflict is 409, unavailable dependency is 503.
- Collection endpoints have deterministic ordering and bounded pagination. Missing domain values remain `null`; they are not invented as zero.
- Mutating retries require a natural/explicit idempotency key or a database uniqueness constraint.
