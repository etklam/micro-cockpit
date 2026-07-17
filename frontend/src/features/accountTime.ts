/**
 * Account-timezone helpers. Uses Intl (IANA) — no hand-rolled DST tables.
 *
 * Ambiguous (fold) wall times: prefer the earlier UTC instant (standard-time /
 * first occurrence). Nonexistent (gap) wall times are rejected.
 */

export type WallTime = {
  year: number
  month: number
  day: number
  hour: number
  minute: number
  second?: number
}

export type AccountTimeError = 'invalid' | 'nonexistent' | 'unsupported_timezone'

const dtfCache = new Map<string, Intl.DateTimeFormat>()

function formatter(timeZone: string): Intl.DateTimeFormat {
  let f = dtfCache.get(timeZone)
  if (!f) {
    f = new Intl.DateTimeFormat('en-US', {
      timeZone,
      hour12: false,
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    })
    dtfCache.set(timeZone, f)
  }
  return f
}

function partsInZone(date: Date, timeZone: string): WallTime | null {
  try {
    const parts = formatter(timeZone).formatToParts(date)
    const get = (type: Intl.DateTimeFormatPartTypes) => {
      const v = parts.find(p => p.type === type)?.value
      return v == null ? NaN : Number(v)
    }
    let hour = get('hour')
    // Some engines emit "24" for midnight.
    if (hour === 24) hour = 0
    return {
      year: get('year'),
      month: get('month'),
      day: get('day'),
      hour,
      minute: get('minute'),
      second: get('second'),
    }
  } catch {
    return null
  }
}

function wallEquals(a: WallTime, b: WallTime, withSeconds: boolean): boolean {
  return a.year === b.year && a.month === b.month && a.day === b.day
    && a.hour === b.hour && a.minute === b.minute
    && (!withSeconds || (a.second ?? 0) === (b.second ?? 0))
}

/** Account-local calendar date (YYYY-MM-DD) for a UTC instant. */
export function accountLocalDate(isoOrDate: string | Date, timeZone: string): string {
  const date = typeof isoOrDate === 'string' ? new Date(isoOrDate) : isoOrDate
  const wall = partsInZone(date, timeZone)
  if (!wall) return ''
  return `${String(wall.year).padStart(4, '0')}-${String(wall.month).padStart(2, '0')}-${String(wall.day).padStart(2, '0')}`
}

/** Account-local `datetime-local` value (YYYY-MM-DDTHH:mm) for a UTC instant. */
export function utcToAccountDateTimeLocal(isoOrDate: string | Date, timeZone: string): string {
  const date = typeof isoOrDate === 'string' ? new Date(isoOrDate) : isoOrDate
  if (Number.isNaN(date.getTime())) return ''
  const wall = partsInZone(date, timeZone)
  if (!wall) return ''
  return `${String(wall.year).padStart(4, '0')}-${String(wall.month).padStart(2, '0')}-${String(wall.day).padStart(2, '0')}T${String(wall.hour).padStart(2, '0')}:${String(wall.minute).padStart(2, '0')}`
}

/**
 * Parse `datetime-local` (YYYY-MM-DDTHH:mm[:ss]) in account timezone → UTC ISO.
 * Returns { ok:false, error } for invalid / nonexistent wall times.
 * Ambiguous fold: earlier UTC instant.
 */
export function accountDateTimeLocalToUtc(
  value: string,
  timeZone: string,
): { ok: true; iso: string } | { ok: false; error: AccountTimeError } {
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})(?::(\d{2}))?$/.exec(value.trim())
  if (!match) return { ok: false, error: 'invalid' }
  const wall: WallTime = {
    year: Number(match[1]),
    month: Number(match[2]),
    day: Number(match[3]),
    hour: Number(match[4]),
    minute: Number(match[5]),
    second: match[6] ? Number(match[6]) : 0,
  }
  if ([wall.year, wall.month, wall.day, wall.hour, wall.minute, wall.second].some(n => !Number.isFinite(n))) {
    return { ok: false, error: 'invalid' }
  }
  // Probe: assume wall is UTC, measure zone offset, correct once, then verify.
  try {
    // Two candidate guesses around the fold (±14h covers all zones).
    const guess = Date.UTC(wall.year, wall.month - 1, wall.day, wall.hour, wall.minute, wall.second ?? 0)
    const candidates: number[] = []
    for (const delta of [0, -14 * 3600_000, 14 * 3600_000, -25 * 3600_000, 25 * 3600_000]) {
      const probe = new Date(guess + delta)
      const asWall = partsInZone(probe, timeZone)
      if (!asWall) return { ok: false, error: 'unsupported_timezone' }
      // Desired UTC = probe + (wall_as_utc - wall_at_probe_as_utc) ... simpler: binary adjust by offset.
      const wallAsUtc = Date.UTC(wall.year, wall.month - 1, wall.day, wall.hour, wall.minute, wall.second ?? 0)
      const probeWallAsUtc = Date.UTC(asWall.year, asWall.month - 1, asWall.day, asWall.hour, asWall.minute, asWall.second ?? 0)
      const utcMs = probe.getTime() + (wallAsUtc - probeWallAsUtc)
      const check = partsInZone(new Date(utcMs), timeZone)
      if (check && wallEquals(check, wall, true)) candidates.push(utcMs)
    }
    const unique = [...new Set(candidates)].sort((a, b) => a - b)
    if (unique.length === 0) return { ok: false, error: 'nonexistent' }
    // Fold: prefer earlier instant.
    return { ok: true, iso: new Date(unique[0]).toISOString() }
  } catch {
    return { ok: false, error: 'unsupported_timezone' }
  }
}

export function accountMonthYear(localDate: string): { year: number; month: number } | null {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(localDate)) return null
  const year = Number(localDate.slice(0, 4))
  const month = Number(localDate.slice(5, 7))
  if (!Number.isFinite(year) || month < 1 || month > 12) return null
  return { year, month }
}

export function formatTimezoneLabel(timeZone: string): string {
  try {
    const parts = new Intl.DateTimeFormat('en-US', { timeZone, timeZoneName: 'shortOffset' }).formatToParts(new Date())
    const offset = parts.find(p => p.type === 'timeZoneName')?.value ?? ''
    return offset ? `${timeZone} (${offset})` : timeZone
  } catch {
    return timeZone
  }
}

export function deviceTimezone(): string {
  return Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC'
}

/** Current account-local datetime-local string (for new transactions). */
export function nowAccountDateTimeLocal(timeZone: string): string {
  return utcToAccountDateTimeLocal(new Date(), timeZone)
}
