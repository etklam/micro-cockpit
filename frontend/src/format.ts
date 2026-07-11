// Formatting + tiny helpers. Stdlib Intl only — no date/number libraries.

/** Join class names; falsy values skipped. */
export const cx = (...parts: Array<string | false | null | undefined>): string =>
  parts.filter(Boolean).join(' ')

/** Today's date as yyyy-mm-dd (matches the API local-date contract). */
export const todayISO = (): string => new Date().toLocaleDateString('en-CA')

const safeDate = (iso: string): Date | null => {
  const d = new Date(`${iso}T00:00:00`)
  return isNaN(d.getTime()) ? null : d
}

export const formatDate = (iso: string): string =>
  safeDate(iso)?.toLocaleDateString(undefined, { weekday: 'short', month: 'short', day: 'numeric' }) ?? iso

export const formatLongDate = (iso: string): string =>
  safeDate(iso)?.toLocaleDateString(undefined, { weekday: 'long', month: 'long', day: 'numeric', year: 'numeric' }) ?? iso

export const monthLabel = (year: number, month1: number): string =>
  new Date(year, month1 - 1, 1).toLocaleDateString(undefined, { month: 'long', year: 'numeric' })

/** "09:00" -> "9:00 AM". */
export const formatTime = (hhmm: string): string => {
  const d = new Date(`1970-01-01T${hhmm}:00`)
  return isNaN(d.getTime()) ? hhmm : d.toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' })
}

/** Absolute money, currency-aware. Falls back to a plain grouped number. */
export const money = (n: number, currency?: string): string =>
  new Intl.NumberFormat(undefined, {
    style: currency ? 'currency' : 'decimal',
    currency,
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(n)

/** Signed money: always carries + / − so direction is never colour-alone. */
export const signed = (n: number, currency?: string): string => {
  if (n === 0 || Number.isNaN(n)) return money(n, currency)
  const sign = n > 0 ? '+' : '−'
  return `${sign}${money(Math.abs(n), currency)}`
}

/** Compact signed value for tight grid cells, e.g. "+1.2K", "−340". */
export const signedCompact = (n: number): string =>
  new Intl.NumberFormat(undefined, {
    notation: 'compact',
    signDisplay: 'exceptZero',
    maximumFractionDigits: 1,
  }).format(n)

/** Signed percent. Pass 2.5 for 2.5%. */
export const pct = (n: number, digits = 2): string =>
  new Intl.NumberFormat(undefined, {
    style: 'percent',
    signDisplay: 'exceptZero',
    minimumFractionDigits: digits,
    maximumFractionDigits: digits,
  }).format(n / 100)

/** A quantity may be fractional; keep it readable, tabular-friendly. */
export const quantity = (n: number): string =>
  new Intl.NumberFormat(undefined, { maximumFractionDigits: 6 }).format(n)

export const repeatLabel = (mode: string): string =>
  mode === 'week' ? 'Weekdays this week' : mode === 'month' ? 'Weekdays this month' : 'Once'
