import { INTL_LOCALE, type Locale, DEFAULT_LOCALE } from './locale'
import { translate } from './translate'

/** Empty/null display — locale-aware dash, never coerced to zero. */
export function emptyValue(locale: Locale = DEFAULT_LOCALE): string {
  return translate(locale, 'common.emptyValue')
}

export function formatDate(iso: string, locale: Locale): string {
  const d = safeDate(iso)
  if (!d) return iso
  return d.toLocaleDateString(INTL_LOCALE[locale], { weekday: 'short', month: 'short', day: 'numeric' })
}

export function formatLongDate(iso: string, locale: Locale): string {
  const d = safeDate(iso)
  if (!d) return iso
  return d.toLocaleDateString(INTL_LOCALE[locale], { weekday: 'long', month: 'long', day: 'numeric', year: 'numeric' })
}

export function monthLabel(year: number, month1: number, locale: Locale): string {
  return new Date(year, month1 - 1, 1).toLocaleDateString(INTL_LOCALE[locale], { month: 'long', year: 'numeric' })
}

/** "09:00" -> locale time. */
export function formatTime(hhmm: string, locale: Locale): string {
  const d = new Date(`1970-01-01T${hhmm}:00`)
  return isNaN(d.getTime()) ? hhmm : d.toLocaleTimeString(INTL_LOCALE[locale], { hour: 'numeric', minute: '2-digit' })
}

export function formatDateTime(iso: string, locale: Locale, timeZone?: string): string {
  const d = new Date(iso)
  if (isNaN(d.getTime())) return iso
  return d.toLocaleString(INTL_LOCALE[locale], {
    dateStyle: 'medium',
    timeStyle: 'short',
    ...(timeZone ? { timeZone } : {}),
  })
}

export function money(n: number, locale: Locale, currency?: string): string {
  return new Intl.NumberFormat(INTL_LOCALE[locale], {
    style: currency ? 'currency' : 'decimal',
    currency,
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(n)
}

export function signed(n: number, locale: Locale, currency?: string): string {
  if (n === 0 || Number.isNaN(n)) return money(n, locale, currency)
  const sign = n > 0 ? '+' : '−'
  return `${sign}${money(Math.abs(n), locale, currency)}`
}

export function signedCompact(n: number, locale: Locale): string {
  return new Intl.NumberFormat(INTL_LOCALE[locale], {
    notation: 'compact',
    signDisplay: 'exceptZero',
    maximumFractionDigits: 1,
  }).format(n)
}

/** Pass 2.5 for 2.5%. */
export function pct(n: number, locale: Locale, digits = 2): string {
  return new Intl.NumberFormat(INTL_LOCALE[locale], {
    style: 'percent',
    signDisplay: 'exceptZero',
    minimumFractionDigits: digits,
    maximumFractionDigits: digits,
  }).format(n / 100)
}

export function quantity(n: number, locale: Locale): string {
  return new Intl.NumberFormat(INTL_LOCALE[locale], { maximumFractionDigits: 6 }).format(n)
}

export function formatNumber(n: number, locale: Locale, options?: Intl.NumberFormatOptions): string {
  return new Intl.NumberFormat(INTL_LOCALE[locale], options).format(n)
}

export function repeatLabel(mode: string, locale: Locale): string {
  if (mode === 'week') return translate(locale, 'alerts.repeat.week')
  if (mode === 'month') return translate(locale, 'alerts.repeat.month')
  return translate(locale, 'alerts.repeat.once')
}

/** Preserve null semantics: missing stays emptyValue, never 0. */
export function moneyOrEmpty(n: number | null | undefined, locale: Locale, currency?: string): string {
  return n == null || Number.isNaN(n) ? emptyValue(locale) : money(n, locale, currency)
}

export function signedOrEmpty(n: number | null | undefined, locale: Locale, currency?: string): string {
  return n == null || Number.isNaN(n) ? emptyValue(locale) : signed(n, locale, currency)
}

export function pctOrEmpty(n: number | null | undefined, locale: Locale, digits = 2): string {
  return n == null || Number.isNaN(n) ? emptyValue(locale) : pct(n, locale, digits)
}

function safeDate(iso: string): Date | null {
  const d = new Date(`${iso}T00:00:00`)
  return isNaN(d.getTime()) ? null : d
}
