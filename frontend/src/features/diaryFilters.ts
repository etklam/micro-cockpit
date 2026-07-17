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

export function normalizeTags(input: string[]): { tags: string[]; error: string } {
  const seen = new Set<string>()
  const tags: string[] = []
  for (const raw of input) {
    const tag = raw.trim().toLowerCase()
    if (!tag) continue
    if (tag.length > 32) return { tags: [], error: 'Each tag must be 32 characters or fewer.' }
    if ([...tag].some(ch => ch.charCodeAt(0) < 32 || ch.charCodeAt(0) === 127)) {
      return { tags: [], error: 'Tags cannot include control characters.' }
    }
    if (!/^[\p{L}\p{N}][\p{L}\p{N}\s\-._/+]*$/u.test(tag)) return { tags: [], error: 'Tags may only use letters, numbers, spaces, and - _ . / +.' }
    if (!seen.has(tag)) {
      seen.add(tag)
      tags.push(tag)
    }
  }
  if (tags.length > 10) return { tags: [], error: 'At most 10 tags per diary.' }
  return { tags, error: '' }
}

function validDate(value: string | null): string {
  if (!value) return ''
  return /^\d{4}-\d{2}-\d{2}$/.test(value) ? value : ''
}
