export type DiaryReviewFilter = 'all' | 'reviewed' | 'unreviewed'

export type DiaryFilters = {
  q: string
  from: string
  to: string
  review: DiaryReviewFilter
  symbol: string
  tag: string
}

export const emptyDiaryFilters: DiaryFilters = {
  q: '',
  from: '',
  to: '',
  review: 'all',
  symbol: '',
  tag: '',
}

export function parseDiaryFilters(search: URLSearchParams): DiaryFilters {
  const reviewRaw = search.get('review')
  const review: DiaryReviewFilter =
    reviewRaw === 'reviewed' || reviewRaw === 'unreviewed' ? reviewRaw : 'all'
  return {
    q: search.get('q')?.trim() ?? '',
    from: validDate(search.get('from')),
    to: validDate(search.get('to')),
    review,
    symbol: (search.get('symbol') ?? '').trim().toUpperCase(),
    tag: (search.get('tag') ?? '').trim().toLowerCase(),
  }
}

export function diaryFiltersToSearch(filters: DiaryFilters): URLSearchParams {
  const next = new URLSearchParams()
  if (filters.q) next.set('q', filters.q)
  if (filters.from) next.set('from', filters.from)
  if (filters.to) next.set('to', filters.to)
  if (filters.review !== 'all') next.set('review', filters.review)
  if (filters.symbol) next.set('symbol', filters.symbol)
  if (filters.tag) next.set('tag', filters.tag)
  return next
}

export function diaryFiltersActive(filters: DiaryFilters): boolean {
  return Boolean(filters.q || filters.from || filters.to || filters.review !== 'all' || filters.symbol || filters.tag)
}

/** Encode diary list search for durable detail returnTo (search only, no path). */
export function encodeDiaryReturnTo(search: string): string {
  const raw = search.startsWith('?') ? search.slice(1) : search
  const bytes = new TextEncoder().encode(raw)
  let bin = ''
  for (const b of bytes) bin += String.fromCharCode(b)
  return btoa(bin).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
}

/** Accept only internal diary list search; invalid → null (caller falls back to /diary). */
export function decodeDiaryReturnTo(value: string | null | undefined): string | null {
  if (!value) return null
  try {
    const padded = value.replace(/-/g, '+').replace(/_/g, '/')
    const pad = padded.length % 4 === 0 ? '' : '='.repeat(4 - (padded.length % 4))
    const bin = atob(padded + pad)
    const bytes = Uint8Array.from(bin, c => c.charCodeAt(0))
    const decoded = new TextDecoder().decode(bytes)
    if (decoded.includes('://') || decoded.startsWith('//') || decoded.includes('\0')) return null
    // Only diary list query params — never a foreign path.
    if (decoded.startsWith('/') && !decoded.startsWith('/diary')) return null
    const search = decoded.startsWith('/diary')
      ? decoded.slice(decoded.indexOf('?') + 1 || decoded.length)
      : decoded.startsWith('?') ? decoded.slice(1) : decoded
    if (search.includes('/') && !search.startsWith('q=') && !search.includes('=')) return null
    return search
  } catch {
    return null
  }
}

export function diaryDetailPath(id: string, listSearch: string): string {
  const encoded = encodeDiaryReturnTo(listSearch)
  return encoded ? `/diary/${id}?returnTo=${encoded}` : `/diary/${id}`
}

export function listPathFromReturnTo(returnTo: string | null | undefined): { pathname: string; search: string } {
  const decoded = decodeDiaryReturnTo(returnTo)
  return { pathname: '/diary', search: decoded ? `?${decoded}` : '' }
}

/** Error is a stable i18n message key (or '') — UI translates via t(error). */
export type TagNormalizeError =
  | ''
  | 'diary.tag.tooLong'
  | 'diary.tag.controlChars'
  | 'diary.tag.invalidChars'
  | 'diary.tag.tooMany'

export function normalizeTags(input: string[]): { tags: string[]; error: TagNormalizeError } {
  const seen = new Set<string>()
  const tags: string[] = []
  for (const raw of input) {
    const tag = raw.trim().toLowerCase()
    if (!tag) continue
    if (tag.length > 32) return { tags: [], error: 'diary.tag.tooLong' }
    if ([...tag].some(ch => ch.charCodeAt(0) < 32 || ch.charCodeAt(0) === 127)) {
      return { tags: [], error: 'diary.tag.controlChars' }
    }
    if (!/^[\p{L}\p{N}][\p{L}\p{N}\s\-._/+]*$/u.test(tag)) return { tags: [], error: 'diary.tag.invalidChars' }
    if (!seen.has(tag)) {
      seen.add(tag)
      tags.push(tag)
    }
  }
  if (tags.length > 10) return { tags: [], error: 'diary.tag.tooMany' }
  return { tags, error: '' }
}

/** Real calendar date only; syntax-valid but impossible dates (2026-02-31) → ''. */
function validDate(value: string | null): string {
  if (!value) return ''
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return ''
  const [y, m, d] = value.split('-').map(Number)
  const dt = new Date(Date.UTC(y, m - 1, d))
  if (dt.getUTCFullYear() !== y || dt.getUTCMonth() !== m - 1 || dt.getUTCDate() !== d) return ''
  return value
}
