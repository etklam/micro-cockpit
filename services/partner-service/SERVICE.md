# Partner Service

Owns human/AI-agent links and independent per-side sharing decisions. Shared records remain in their owning service; transactions default private.

## Human invitations

- `POST /internal/partners/invitations` creates a single-use code (shown once). Only `SHA-256` of the code is stored.
- Codes expire after 7 days; creator may revoke unused codes.
- `POST /internal/partners/invitations/redeem` creates an **accepted** human link. Self-redeem, reuse, expiry, revoke, and duplicate active pairs fail with generic `invalid_invitation`.

## Authorization contract

- `GET /internal/partners/{ownerId}/authorization?resource=diary|transaction|performance`
- Journal calls this before serving `/internal/partner-diaries` (Journal owns data; Partner owns share decisions).

## Display names

Partner resolves partner display names via Identity `GET /internal/users/display-names?ids=...` (no Identity schema reads).
