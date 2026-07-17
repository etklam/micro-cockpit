# Security review checklist

- [ ] Authentication verifies issuer, audience, signature, expiry, and key rotation; authorization derives ownership from JWT `sub`.
- [ ] Browser receives no internal service URL, service key, database credential, signing key, stack trace, or sensitive log field.
- [ ] PostgreSQL roles match `contracts/schema-ownership.json`; no service has cross-schema DML or database-owner privileges.
- [ ] Input is bounded and parameterized; uploads, URLs, redirects, and rendered content receive context-specific validation.
- [ ] Cross-owner resource probing returns the same result as missing resources.
- [ ] Admin/worker endpoints require a separately rotated secret or admin claim and fail closed.
- [x] Refresh tokens, logout, registration controls, rate limits, CORS, TLS, and secure cookie/storage choices are reviewed (Edge IP rate limits on anonymous auth; public registration default false; registration-key opt-in forward).
- [ ] Events minimize personal data, are idempotent, and reject unsupported versions safely.
- [ ] Dependencies and container bases are scanned; containers run without root and with minimal filesystem/network access where practical.
- [ ] Backup encryption, restore verification, retention, secret rotation, incident response, and audit evidence have named owners.
