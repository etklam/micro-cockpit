import { catalogs, type MessageKey } from './messages'
import { DEFAULT_LOCALE, type Locale } from './locale'

export type TranslateVars = Record<string, string | number | null | undefined>

const missing = new Set<string>()

function warnMissing(locale: Locale, key: string) {
  if (import.meta.env?.PROD) return
  const id = `${locale}:${key}`
  if (missing.has(id)) return
  missing.add(id)
  console.warn(`[i18n] missing key "${key}" for locale "${locale}"`)
}

function interpolate(template: string, vars?: TranslateVars): string {
  if (!vars) return template
  return template.replace(/\{(\w+)\}/g, (_, name: string) => {
    const value = vars[name]
    return value == null ? '' : String(value)
  })
}

/**
 * Resolve a message for locale with English fallback.
 * Plural: if vars.count is a number and != 1, try `${key}_other` first.
 */
export function translate(locale: Locale, key: MessageKey, vars?: TranslateVars): string {
  const count = vars?.count
  const pluralKey = typeof count === 'number' && count !== 1
    ? (`${key}_other` as MessageKey)
    : null

  const table = catalogs[locale] ?? catalogs[DEFAULT_LOCALE]
  const enTable = catalogs[DEFAULT_LOCALE]

  if (pluralKey) {
    const plural = table[pluralKey] ?? enTable[pluralKey]
    if (plural) return interpolate(plural, vars)
  }

  const direct = table[key]
  if (direct) return interpolate(direct, vars)

  const fallback = enTable[key]
  if (fallback) {
    if (locale !== DEFAULT_LOCALE) warnMissing(locale, key)
    return interpolate(fallback, vars)
  }

  warnMissing(locale, key)
  return key
}

/** Stateless helper for non-React modules (error mappers, format labels). */
export function createTranslator(locale: Locale) {
  return (key: MessageKey, vars?: TranslateVars) => translate(locale, key, vars)
}
