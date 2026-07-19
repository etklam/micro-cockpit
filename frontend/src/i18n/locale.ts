/** Supported UI locales. Unknown values fall back to English. */
export type Locale = 'en' | 'zh-Hant'

export const LOCALES: readonly Locale[] = ['en', 'zh-Hant'] as const
export const DEFAULT_LOCALE: Locale = 'en'
export const LOCALE_STORAGE_KEY = 'td_locale'

/** BCP 47 tags used by Intl formatters. */
export const INTL_LOCALE: Record<Locale, string> = {
  en: 'en-US',
  'zh-Hant': 'zh-Hant-TW',
}

export function isLocale(value: unknown): value is Locale {
  return value === 'en' || value === 'zh-Hant'
}

/** Normalize any stored/API/browser tag to a supported Locale. */
export function normalizeLocale(value: unknown): Locale {
  if (isLocale(value)) return value
  if (typeof value !== 'string' || !value.trim()) return DEFAULT_LOCALE
  const raw = value.trim().replace(/_/g, '-')
  const lower = raw.toLowerCase()
  if (lower === 'en' || lower.startsWith('en-')) return 'en'
  // Traditional Chinese family (and common aliases).
  if (
    lower === 'zh-hant'
    || lower === 'zh-tw'
    || lower === 'zh-hk'
    || lower === 'zh-mo'
    || lower.startsWith('zh-hant')
    || lower === 'zh-cht'
  ) return 'zh-Hant'
  // Bare "zh" is ambiguous; prefer English rather than assuming script.
  return DEFAULT_LOCALE
}

export function readLocaleMirror(): Locale | null {
  try {
    const raw = localStorage.getItem(LOCALE_STORAGE_KEY)
    return raw == null ? null : (isLocale(raw) ? raw : null)
  } catch {
    return null
  }
}

export function writeLocaleMirror(locale: Locale) {
  try { localStorage.setItem(LOCALE_STORAGE_KEY, locale) } catch { /* private mode */ }
}

export function detectBrowserLocale(): Locale {
  try {
    const languages = typeof navigator !== 'undefined'
      ? (navigator.languages?.length ? navigator.languages : [navigator.language])
      : []
    for (const tag of languages) {
      const normalized = normalizeLocale(tag)
      // Only accept when the tag actually matched a supported family.
      if (isLocale(tag) || (typeof tag === 'string' && (
        tag.toLowerCase().startsWith('en')
        || tag.toLowerCase().includes('hant')
        || /zh[-_](tw|hk|mo)/i.test(tag)
      ))) return normalized
    }
  } catch { /* ignore */ }
  return DEFAULT_LOCALE
}

/** Anonymous/pre-login resolution: mirror → browser → English. */
export function resolveAnonymousLocale(): Locale {
  return readLocaleMirror() ?? detectBrowserLocale()
}

/** Apply document language for a11y/CSS. */
export function applyDocumentLocale(locale: Locale) {
  document.documentElement.lang = locale === 'zh-Hant' ? 'zh-Hant' : 'en'
}

export function bootLocaleFromMirror() {
  applyDocumentLocale(resolveAnonymousLocale())
}

/** Server preference wins for authenticated users; also mirrors for login page. */
export function reconcileLocale(server: unknown): Locale | null {
  if (!isLocale(server)) return null
  writeLocaleMirror(server)
  applyDocumentLocale(server)
  return server
}

/** Logout: keep last mirrored locale on anonymous pages. */
export function localeOnLogout() {
  applyDocumentLocale(resolveAnonymousLocale())
}
