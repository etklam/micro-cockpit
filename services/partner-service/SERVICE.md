# Partner Service

Owns human/AI-agent links and independent per-side sharing decisions. Shared records remain in their owning service; transactions default private.

## Human invitations (invitation-only human creation)

Human partner creation is **invitation-only**. There is no raw browser `POST /api/app/partners` create path.

- `POST /internal/partners/invitations` creates a single-use code (shown once). Only `SHA-256` of the code is stored.
- Codes expire after 7 days; the creator may revoke unused codes.
- `POST /internal/partners/invitations/redeem` creates an **accepted** human link and sets **`acceptedAt`**. Self-redeem, reuse, expiry, revoke, and duplicate active pairs fail with generic `invalid_invitation`.

Legacy pending rows remain listable, revocable, and accept-able through `POST /internal/partners/{id}/accept`, which also sets **`acceptedAt`**. There is no raw UUID create endpoint. Agent creation remains owned by Identity.

## acceptedAt semantics

| Event | `status` | `acceptedAt` |
|---|---|---|
| Invitation redeem | `accepted` | Set at redeem time |
| Pending link accept | `accepted` | Set when accept succeeds |
| Pending (not yet accepted) | `pending` | `null` |
| Revoked | `revoked` | Unchanged if previously accepted; otherwise `null` |

Compare and share-policy require `status=accepted` (non-null `acceptedAt` on accepted links). Non-members and revoked links are concealed as `404` where applicable.

## Identity display degradation

Partner resolves partner display names via Identity `GET /internal/users/display-names?ids=...` (no Identity schema reads). On Identity failure, timeout, or missing ids, **`partnerDisplayName` is `null`** (nullable degradation). List/summary still succeed; the UI labels the partner locally.

## Authorization contract

- `GET /internal/partners/{ownerId}/authorization?resource=diary|transaction|performance`
- Journal calls this before serving `/internal/partner-diaries` (Journal owns data; Partner owns share decisions).
- UI v1 only exposes `shareDiaries`; transaction/performance share flags stay false unless set by API.

## Edge → Partner → Journal authorization (compare)

Browser never calls Journal partner reads directly:

1. Edge `GET /api/app/partners/{linkId}/compare` loads Partner summary (accepted member only).
2. If the partner side has `shareDiaries`, Edge calls Journal `GET /internal/partner-diaries`.
3. Journal re-checks Partner authorization and returns a sanitized diary projection only.

Unauthorized or non-shared access is non-disclosing (`404` / `partnerDiaries=not_shared`), not partial private fields.

## Browser DTO privacy

Partner-facing diary items expose only: `id`, `localDate`, `title`, `content`, `tags`.

Never returned on partner/compare paths: transactions, review fields (`thesis`, scores, emotions, mistake tags, lessons), notes, user ids, or idempotency metadata.

## Inclusive 366-day range

Compare and partner-diary windows allow an **inclusive 366-day** span: `to.DayNumber - from.DayNumber <= 365`. Larger windows return `400`. Default compare window when omitted is the last 30 local days (`from = to - 29 days`).
