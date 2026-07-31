import { describe, expect, test } from 'vitest'
import {
  accountDateTimeLocalToUtc,
  accountLocalDate,
  accountLocalHour,
  accountMonthYear,
  utcToAccountDateTimeLocal,
} from '../features/accountTime'

describe('account time conversion', () => {
  test('UTC round-trip', () => {
    const local = '2026-07-18T12:30'
    const utc = accountDateTimeLocalToUtc(local, 'UTC')
    expect(utc.ok).toBe(true)
    if (!utc.ok) return
    expect(utc.iso).toBe('2026-07-18T12:30:00.000Z')
    expect(utcToAccountDateTimeLocal(utc.iso, 'UTC')).toBe(local)
  })

  test('Asia/Tokyo conversion', () => {
    const local = '2026-07-18T09:00'
    const utc = accountDateTimeLocalToUtc(local, 'Asia/Tokyo')
    expect(utc.ok).toBe(true)
    if (!utc.ok) return
    expect(utc.iso).toBe('2026-07-18T00:00:00.000Z')
    expect(utcToAccountDateTimeLocal(utc.iso, 'Asia/Tokyo')).toBe(local)
    expect(accountLocalDate(utc.iso, 'Asia/Tokyo')).toBe('2026-07-18')
  })

  test('America/New_York conversion', () => {
    const local = '2026-01-15T12:00'
    const utc = accountDateTimeLocalToUtc(local, 'America/New_York')
    expect(utc.ok).toBe(true)
    if (!utc.ok) return
    expect(utcToAccountDateTimeLocal(utc.iso, 'America/New_York')).toBe(local)
  })

  test('DST gap is rejected (America/New_York spring forward)', () => {
    // 2026-03-08 02:30 does not exist in America/New_York
    const result = accountDateTimeLocalToUtc('2026-03-08T02:30', 'America/New_York')
    expect(result.ok).toBe(false)
    if (result.ok) return
    expect(result.error).toBe('nonexistent')
  })

  test('DST fold prefers earlier instant (America/New_York fall back)', () => {
    // 2026-11-01 01:30 occurs twice; prefer earlier UTC
    const result = accountDateTimeLocalToUtc('2026-11-01T01:30', 'America/New_York')
    expect(result.ok).toBe(true)
    if (!result.ok) return
    // EDT (UTC-4) is earlier than EST (UTC-5)
    expect(result.iso).toBe('2026-11-01T05:30:00.000Z')
  })

  test('month/year from account local date', () => {
    expect(accountMonthYear('2026-07-18')).toEqual({ year: 2026, month: 7 })
  })

  test('hour follows the account timezone and falls back to UTC', () => {
    const instant = new Date('2026-07-18T03:30:00.000Z')
    expect(accountLocalHour(instant, 'Asia/Taipei')).toBe(11)
    expect(accountLocalHour(instant, '')).toBe(3)
  })
})
