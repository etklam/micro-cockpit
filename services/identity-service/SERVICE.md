# Identity Service

Owns `identity.*`: users, password credentials, and rotating refresh tokens.

- Registration has three explicit modes: public when `Auth__AllowPublicRegistration=true`, key-gated when public registration is false and `Auth__LocalRegistrationKey` is set, and disabled when both public registration and the key are absent. Code and deployment defaults keep public registration off.
- Registration validates email shape/length, display name, password length (12–256), IANA timezone, and three-letter currency; passwords use per-user PBKDF2-SHA256 (210,000 iterations) salts and are never logged or returned.
- Edge is responsible for anonymous auth rate limiting and for forwarding `X-Registration-Key` only to this service's register route.
- Refresh tokens are random, stored only as SHA-256 hashes, single-use, and grouped into revocable families.
- Access tokens carry subject, role, account type, and status version.
- AI agents are normal `account_type=agent` users. Scoped API keys are stored only as SHA-256 hashes and exchange for short-lived RSA access tokens.
