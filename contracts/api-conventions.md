# API conventions

- Browser traffic terminates at the Edge API. Internal service URLs are never shipped to the frontend.
- Public application routes use `/api/app/*`; authentication uses `/api/auth/*`, anonymous content uses `/api/content/*`, and administration uses `/api/admin/*`. Service routes use `/internal/*`. Breaking service or published-view contracts require a new versioned path or schema.
- Authenticate with `Authorization: Bearer <JWT>`. Ownership comes from verified `sub`, never a caller-supplied user ID.
- Forward or create `X-Correlation-Id`; return it on responses and include it in structured logs.
- JSON uses camelCase and UTC RFC 3339 timestamps. Local calendar dates use `YYYY-MM-DD` with an explicit IANA timezone where conversion matters.
- Edge errors use `application/problem+json` with `code`, `title`, `status`, safe `detail`, and `correlationId`. Stable codes are `invalid_request`, `authentication_required`, `access_denied`, `resource_not_found`, `conflict`, `validation_failed`, `downstream_invalid_response`, `downstream_unavailable`, and `downstream_timeout`.
- Collection endpoints have deterministic ordering and bounded pagination. Missing domain values remain `null`; they are not invented as zero.
- Mutating retries require a natural/explicit idempotency key or a database uniqueness constraint.
- Downstream timeouts return 504, invalid typed payloads return 502, and unavailable required services return 503. These failures must not be converted to valid empty responses.
- OpenAPI comes from runtime DTOs and endpoint metadata. The frontend client comes from the composed Edge document and must never be edited manually.

See [API and data reference](../docs/reference-api-data.md) for the complete public route and error reference.
