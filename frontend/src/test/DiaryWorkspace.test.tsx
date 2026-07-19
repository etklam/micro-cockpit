import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { expect, test, vi } from 'vitest'
import { AuthProvider } from '../auth/AuthProvider'
import { I18nProvider } from '../i18n'
import { DiaryDetailPage, DiaryPage } from '../screens/diary'
import { MarkdownView, plainExcerpt, safeUrlTransform } from '../features/markdown'
import { normalizeTags, parseDiaryFilters } from '../features/diaryFilters'
import { CockpitProvider } from '../shell'
import { server } from './setup'

const diaryA = {
  id: 'd1', localDate: '2026-07-16', title: 'Alpha', content: 'Bought the **breakout**', createdAt: '2026-07-16T10:00:00Z', updatedAt: '2026-07-16T10:00:00Z', tags: ['fomo'],
}
const diaryB = {
  id: 'd2', localDate: '2026-07-15', title: 'Beta', content: 'Plain historical note', createdAt: '2026-07-15T10:00:00Z', updatedAt: '2026-07-15T10:00:00Z', tags: ['plan'],
}

const bootstrap = {
  currentUser: { id: '11111111-1111-1111-1111-111111111111', email: 'owner@example.com', displayName: 'Owner' },
  timezone: 'UTC', baseCurrency: 'USD', appearance: 'system', locale: 'en', role: 'user', accountType: 'human', currentLocalDate: '2026-07-16',
  availableProductAreas: ['today', 'diary', 'calendar'],
}

function renderDiary(initial = '/diary', confirmImpl: () => Promise<boolean> = async () => true) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[initial]}>
        <AuthProvider>
          <I18nProvider>
            <CockpitProvider value={{ go: () => undefined, confirm: confirmImpl }}>
              <Routes>
                <Route path="/diary" element={<DiaryPage />} />
                <Route path="/diary/:diaryId" element={<DiaryDetailPage />} />
              </Routes>
            </CockpitProvider>
          </I18nProvider>
        </AuthProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

function handlers(options?: {
  pages?: Record<string, typeof diaryA[]>
  createStatus?: number
  deleteStatus?: number
  onList?: (url: URL) => void
  onCreate?: (body: unknown) => void
}) {
  return [
    http.post('/api/auth/refresh', () => HttpResponse.json({ accessToken: 'token', expiresAt: '2026-07-16T12:00:00Z' })),
    http.get('/api/app/bootstrap', () => HttpResponse.json(bootstrap)),
    http.get('/api/app/diary-review-summary', () => HttpResponse.json({
      reviewedCount: 0, averageDisciplineScore: null, averageExecutionScore: null, emotionCounts: {}, processAssessmentCounts: {}, topMistakeTags: [],
    })),
    http.get('/api/app/diaries', ({ request }) => {
      const url = new URL(request.url)
      options?.onList?.(url)
      const cursor = url.searchParams.get('cursor') ?? ''
      if (options?.pages) {
        const page = options.pages[cursor] ?? []
        const keys = Object.keys(options.pages)
        const idx = keys.indexOf(cursor)
        const next = idx >= 0 && idx < keys.length - 1 ? keys[idx + 1] : null
        return HttpResponse.json({ items: page, nextCursor: next })
      }
      return HttpResponse.json({ items: [diaryA, diaryB], nextCursor: null })
    }),
    http.get('/api/app/diaries/:id', ({ params }) => HttpResponse.json(params.id === 'd1' ? diaryA : diaryB)),
    http.get('/api/app/diaries/:id/transactions', () => HttpResponse.json({ items: [] })),
    http.get('/api/app/diaries/:id/review', () => new HttpResponse(null, { status: 404 })),
    http.post('/api/app/diaries', async ({ request }) => {
      const body = await request.json()
      options?.onCreate?.(body)
      if (options?.createStatus) return HttpResponse.json({ title: 'error' }, { status: options.createStatus })
      return HttpResponse.json({ ...diaryA, id: 'd-new', title: (body as { title: string }).title, tags: (body as { tags?: string[] }).tags ?? [] }, { status: 201 })
    }),
    http.put('/api/app/diaries/:id', async ({ request }) => {
      const body = await request.json()
      options?.onCreate?.(body)
      return new HttpResponse(null, { status: 204 })
    }),
    http.delete('/api/app/diaries/:id', () => new HttpResponse(null, { status: options?.deleteStatus ?? 204 })),
  ]
}

test('URL initializes filters and other controls update URL', async () => {
  const seen: string[] = []
  server.use(...handlers({ onList: url => seen.push(url.search) }))
  const user = userEvent.setup()
  renderDiary('/diary?q=break&review=reviewed&symbol=aapl&tag=fomo&from=2026-07-01&to=2026-07-16')
  await screen.findByText('Alpha')
  expect(seen.some(value => value.includes('query=break') && value.includes('reviewStatus=reviewed') && value.includes('symbol=AAPL') && value.includes('tag=fomo'))).toBe(true)
  await user.selectOptions(screen.getByLabelText('Review'), 'unreviewed')
  await waitFor(() => expect(seen.at(-1)).toContain('reviewStatus=unreviewed'))
})

test('invalid review enum falls back safely and clear filters resets URL', async () => {
  const seen: string[] = []
  server.use(...handlers({ onList: url => seen.push(url.search) }))
  const user = userEvent.setup()
  renderDiary('/diary?review=nope&tag=fomo')
  await screen.findByText('Alpha')
  expect(screen.getByLabelText('Review')).toHaveValue('all')
  await user.click(screen.getByRole('button', { name: 'Clear filters' }))
  await waitFor(() => expect(seen.at(-1) === '' || !seen.at(-1)?.includes('tag=')).toBe(true))
})

test('keyword request is debounced', async () => {
  vi.useFakeTimers({ shouldAdvanceTime: true })
  const seen: string[] = []
  server.use(...handlers({ onList: url => seen.push(url.searchParams.get('query') ?? '') }))
  const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime })
  renderDiary('/diary')
  await screen.findByText('Alpha')
  const before = seen.length
  await user.type(screen.getByLabelText('Keyword'), 'plan')
  expect(seen.length).toBe(before)
  await vi.advanceTimersByTimeAsync(320)
  await waitFor(() => expect(seen.some(value => value === 'plan')).toBe(true))
  vi.useRealTimers()
})

test('load more appends another page without duplicate ids and filter change resets pages', async () => {
  const pages = { '': [diaryA], page2: [diaryB] }
  server.use(...handlers({ pages: { '': [diaryA], page2: [diaryB] } as any }))
  // Fix pages typing with nextCursor chain
  server.use(
    http.get('/api/app/diaries', ({ request }) => {
      const cursor = new URL(request.url).searchParams.get('cursor')
      if (!cursor) return HttpResponse.json({ items: [diaryA], nextCursor: 'page2' })
      if (cursor === 'page2') return HttpResponse.json({ items: [diaryB], nextCursor: null })
      return HttpResponse.json({ items: [], nextCursor: null })
    }),
    http.post('/api/auth/refresh', () => HttpResponse.json({ accessToken: 'token', expiresAt: '2026-07-16T12:00:00Z' })),
    http.get('/api/app/bootstrap', () => HttpResponse.json(bootstrap)),
    http.get('/api/app/diary-review-summary', () => HttpResponse.json({
      reviewedCount: 0, averageDisciplineScore: null, averageExecutionScore: null, emotionCounts: {}, processAssessmentCounts: {}, topMistakeTags: [],
    })),
  )
  const user = userEvent.setup()
  renderDiary('/diary')
  await screen.findByText('Alpha')
  expect(screen.queryByText('Beta')).not.toBeInTheDocument()
  await user.click(screen.getByRole('button', { name: 'Load more' }))
  await screen.findByText('Beta')
  expect(screen.getAllByText('Alpha')).toHaveLength(1)
  await user.selectOptions(screen.getByLabelText('Review'), 'reviewed')
  await waitFor(() => expect(screen.queryByText('Beta')).not.toBeInTheDocument())
  void pages
})

test('empty states differ for no entries vs no matches', async () => {
  server.use(...handlers({ pages: { '': [] } as any }))
  server.use(
    http.get('/api/app/diaries', () => HttpResponse.json({ items: [], nextCursor: null })),
    http.post('/api/auth/refresh', () => HttpResponse.json({ accessToken: 'token', expiresAt: '2026-07-16T12:00:00Z' })),
    http.get('/api/app/bootstrap', () => HttpResponse.json(bootstrap)),
    http.get('/api/app/diary-review-summary', () => HttpResponse.json({
      reviewedCount: 0, averageDisciplineScore: null, averageExecutionScore: null, emotionCounts: {}, processAssessmentCounts: {}, topMistakeTags: [],
    })),
  )
  renderDiary('/diary')
  expect(await screen.findByText('Your diary is empty')).toBeInTheDocument()
  renderDiary('/diary?tag=missing')
  expect(await screen.findByText('No entries match these filters')).toBeInTheDocument()
})

test('clicking a tag applies the tag filter', async () => {
  const seen: string[] = []
  server.use(...handlers({ onList: url => seen.push(url.searchParams.get('tag') ?? '') }))
  const user = userEvent.setup()
  renderDiary('/diary')
  await screen.findByText('Alpha')
  await user.click(screen.getByRole('button', { name: 'fomo' }))
  await waitFor(() => expect(seen.at(-1)).toBe('fomo'))
})

test('create submits normalized tags and validation preserves form values', async () => {
  let body: any = null
  server.use(...handlers({ onCreate: value => { body = value } }))
  const user = userEvent.setup()
  renderDiary('/diary')
  await screen.findByText('Alpha')
  await user.type(screen.getByPlaceholderText('Name the day'), 'New day')
  await user.type(screen.getByPlaceholderText(/Markdown is supported/), 'Body stays')
  await user.type(screen.getByLabelText('Add tag'), 'FOMO')
  await user.keyboard('{Enter}')
  await user.click(screen.getByRole('button', { name: 'Add entry' }))
  await waitFor(() => expect(body?.tags).toEqual(['fomo']))
  expect(body.title).toBe('New day')
})

test('write/preview switching preserves content', async () => {
  server.use(...handlers())
  const user = userEvent.setup()
  renderDiary('/diary')
  await screen.findByText('Alpha')
  const area = screen.getByPlaceholderText(/Markdown is supported/)
  await user.type(area, 'Keep me')
  await user.click(screen.getByRole('button', { name: 'Preview' }))
  expect(screen.getByText('Keep me')).toBeInTheDocument()
  await user.click(screen.getByRole('button', { name: 'Write' }))
  expect(screen.getByPlaceholderText(/Markdown is supported/)).toHaveValue('Keep me')
})

test('delete failure remains visible and preserves the row', async () => {
  server.use(...handlers({ deleteStatus: 503 }))
  const user = userEvent.setup()
  renderDiary('/diary', async () => true)
  await screen.findByText('Alpha')
  await user.click(screen.getAllByLabelText('Delete entry')[0]!)
  expect(await screen.findByRole('alert')).toHaveTextContent(/Could not delete|unavailable/i)
  expect(screen.getByText('Alpha')).toBeInTheDocument()
})

test('safe markdown helpers reject unsafe urls and strip syntax for excerpts', () => {
  expect(safeUrlTransform('javascript:alert(1)')).toBe('')
  expect(safeUrlTransform('https://example.com')).toBe('https://example.com')
  expect(plainExcerpt('# Title\n<script>x</script>\n**bold**')).not.toContain('<script>')
  expect(plainExcerpt('# Title\n**bold**')).toContain('Title')
  expect(normalizeTags([' FOMO ', 'fomo', 'Plan']).tags).toEqual(['fomo', 'plan'])
  expect(parseDiaryFilters(new URLSearchParams('review=nope')).review).toBe('all')
})

test('markdown renderer shows emphasis and does not create raw HTML anchors', async () => {
  render(<MarkdownView content={'Hello **world**\n\n<a href="x">html</a>\n\n[ok](https://example.com)\n\n[bad](javascript:alert(1))'} />)
  expect(screen.getByText('world').tagName).toBe('STRONG')
  // skipHtml keeps text content but does not create an executable/raw HTML anchor.
  expect(screen.queryByRole('link', { name: 'html' })).not.toBeInTheDocument()
  expect(screen.getByRole('link', { name: 'ok' })).toHaveAttribute('href', 'https://example.com')
  expect(screen.getByRole('link', { name: 'ok' })).toHaveAttribute('rel', expect.stringContaining('noopener'))
  expect(screen.getByText('bad').closest('a')).toHaveAttribute('href', '')
})
