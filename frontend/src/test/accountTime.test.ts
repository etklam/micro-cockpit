import { describe, expect, test } from 'vitest'
import {
  accountDateTimeLocalToUtc,
  accountLocalDate,
  accountMonthYear,
  utcToAccountDateTimeLocal,
} from '../features/accountTime'
import {
  decodeDiaryReturnTo,
  encodeDiaryReturnTo,
  parseDiaryFilters,
} from '../features/diaryFilters'

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
})

describe('diary filters normalization', () => {
  test('blank tag is absent', () => {
    const filters = parseDiaryFilters(new URLSearchParams('tag='))
    expect(filters.tag).toBe('')
  })

  test('impossible dates are dropped', () => {
    const filters = parseDiaryFilters(new URLSearchParams('from=2026-02-31&to=2026-13-01'))
    expect(filters.from).toBe('')
    expect(filters.to).toBe('')
  })

  test('returnTo encodes and only accepts diary search', () => {
    const encoded = encodeDiaryReturnTo('symbol=AAPL&tag=fomo')
    expect(decodeDiaryReturnTo(encoded)).toBe('symbol=AAPL&tag=fomo')
    expect(decodeDiaryReturnTo(encodeURIComponent('https://evil.example/x'))).toBeNull()
    // path outside /diary
    const evil = btoa('/admin').replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
    expect(decodeDiaryReturnTo(evil)).toBeNull()
  })
})
