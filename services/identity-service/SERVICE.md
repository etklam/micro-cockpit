# Identity Service

Owns `identity.*`: users, password credentials, and rotating refresh tokens.

- Local registration requires the deployment registration key; public signup is disabled.
- Passwords use per-user PBKDF2-SHA256 salts and are never logged or returned.
- Refresh tokens are random, stored only as SHA-256 hashes, single-use, and grouped into revocable families.
- Access tokens carry subject, role, account type, and status version.
