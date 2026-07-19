# ADR: Lightweight typed frontend i18n

## Status

Accepted (2026-07-20)

## Context

Production replacement requires English and Traditional Chinese for the diary workflow. The codebase already persists account preferences (`timezone`, `baseCurrency`, `appearance`) through Identity → Edge → bootstrap/settings. No i18n library was installed.

## Decision

Use a small internal typed i18n layer instead of `react-i18next` / FormatJS:

- Stable message keys (not English-as-key).
- Catalogs: `en`, `zh-Hant`.
- `I18nProvider` + `useI18n` / `t`.
- English fallback and dev missing-key warnings.
- Locale-aware `Intl` formatters centralized in `i18n/format.ts`.
- Authenticated locale stored on `identity.users.locale` (migration `0020`), exposed on bootstrap and settings.
- Anonymous preference mirrored in `localStorage` (`td_locale`) only.

## Consequences

- No new runtime dependency.
- Adding ICU-rich plurals/select later may justify a library; simple `key` / `key_other` is enough for this phase.
- OpenAPI regen required when settings/bootstrap DTOs change.
