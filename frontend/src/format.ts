// Formatting + tiny helpers. Stdlib Intl only — no date/number libraries.
// Locale-aware via active locale set by I18nProvider (falls back to mirror/en).

import {
  DEFAULT_LOCALE,
  resolveAnonymousLocale,
  type Locale,
} from './i18n/locale'
import * as i18nFormat from './i18n/format'

let activeLocale: Locale = (() => {
  try { return resolveAnonymousLocale() } catch { return DEFAULT_LOCALE }
})()

/** Called by I18nProvider when locale changes. */
export function setActiveFormatLocale(locale: Locale) {
  activeLocale = locale
}

export function getActiveFormatLocale(): Locale {
  return activeLocale
}

/** Join class names; falsy values skipped. */
export const cx = (...parts: Array<string | false | null | undefined>): string =>
  parts.filter(Boolean).join(' ')

/** Today's date as yyyy-mm-dd (matches the API local-date contract). */
export const todayISO = (): string => new Date().toLocaleDateString('en-CA')

export const formatDate = (iso: string): string => i18nFormat.formatDate(iso, activeLocale)
export const formatLongDate = (iso: string): string => i18nFormat.formatLongDate(iso, activeLocale)
export const monthLabel = (year: number, month1: number): string => i18nFormat.monthLabel(year, month1, activeLocale)
export const formatTime = (hhmm: string): string => i18nFormat.formatTime(hhmm, activeLocale)
export const money = (n: number, currency?: string): string => i18nFormat.money(n, activeLocale, currency)
export const signed = (n: number, currency?: string): string => i18nFormat.signed(n, activeLocale, currency)
export const signedCompact = (n: number): string => i18nFormat.signedCompact(n, activeLocale)
export const pct = (n: number, digits = 2): string => i18nFormat.pct(n, activeLocale, digits)
export const quantity = (n: number): string => i18nFormat.quantity(n, activeLocale)
export const repeatLabel = (mode: string): string => i18nFormat.repeatLabel(mode, activeLocale)
export const emptyValue = (): string => i18nFormat.emptyValue(activeLocale)
