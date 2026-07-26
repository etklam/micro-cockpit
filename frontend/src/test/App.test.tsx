import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { BrowserRouter } from 'react-router-dom'
import { expect, test } from 'vitest'
import App from '../App'
import type { ObservationUpdateResponse, ObservationUpdateWrite } from '../generated/edge'
import { AuthProvider } from '../auth/AuthProvider'
import { I18nProvider } from '../i18n'
import { server } from './setup'

const bootstrap = {
  currentUser: { id: '11111111-1111-1111-1111-111111111111', email: 'owner@example.com', displayName: 'Owner' },
  timezone: 'Asia/Taipei', journalDayRollover: '00:00', baseCurrency: 'USD', appearance: 'system', accentTheme: 'green', locale: 'en', role: 'user', accountType: 'human', currentLocalDate: '2026-07-16',
  availableProductAreas: ['today', 'diary', 'calendar'],
}

function authenticatedHandlers() {
  return [
    http.post('/api/auth/refresh', () => HttpResponse.json({ accessToken: 'memory-only-token', expiresAt: '2026-07-16T12:00:00Z' })),
    http.get('/api/app/bootstrap', () => HttpResponse.json(bootstrap)),
    http.get('/api/app/dashboard', () => HttpResponse.json({
      localDate: '2026-07-16', diary: { writtenToday: false, count: 0 }, performance: null,
      pendingAlerts: null, discipline: null, recentDiaries: [], capabilities: { alerts: 'unavailable', discipline: 'empty' },
    })),
    http.get('/api/app/calendar', ({ request }) => {
      const url = new URL(request.url)
      return HttpResponse.json({ year: Number(url.searchParams.get('year')), month: Number(url.searchParams.get('month')), summary: null, days: [], capabilities: { alerts: 'unavailable' } })
    }),
    http.get('/api/app/diaries', () => HttpResponse.json({ items: [], nextCursor: null })),
    http.get('/api/app/market-observations/today', () => new HttpResponse(null, { status: 404 })),
    http.get('/api/app/diary-review-summary', () => HttpResponse.json({ reviewedCount: 0, averageDisciplineScore: null, averageExecutionScore: null, emotionCounts: {}, processAssessmentCounts: {}, topMistakeTags: [] })),
    http.get('/api/app/diary-review-items', () => HttpResponse.json({ items: [], nextCursor: null })),
    http.post('/api/auth/logout', () => new HttpResponse(null, { status: 204 })),
  ]
}

function renderApp(path: string) {
  window.history.replaceState({}, '', path)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(<QueryClientProvider client={client}><BrowserRouter><AuthProvider><I18nProvider><App /></I18nProvider></AuthProvider></BrowserRouter></QueryClientProvider>)
  return client
}

test('public landing introduces the product and free tools', async () => {
  server.use(http.post('/api/auth/refresh', () => new HttpResponse(null, { status: 401 })))
  renderApp('/')
  expect(await screen.findByRole('heading', { name: 'A quiet cockpit for reflection.' })).toBeInTheDocument()
  expect(screen.getByRole('link', { name: 'Try tools free' })).toHaveAttribute('href', '/tools?tool=position-sizing')
  expect(screen.getByRole('link', { name: 'Open tools' })).toHaveAttribute('href', '/tools?tool=position-sizing')
  expect(screen.getByRole('heading', { name: 'How it works' })).toBeInTheDocument()
  expect(screen.getByLabelText('Preview')).toBeInTheDocument()
})

test('tools page is usable without signing in', async () => {
  server.use(http.post('/api/auth/refresh', () => new HttpResponse(null, { status: 401 })))
  renderApp('/tools?tool=risk-reward')
  expect(await screen.findByRole('heading', { name: 'Tools' })).toBeInTheDocument()
  expect(screen.getByRole('heading', { name: 'Risk / reward' })).toBeInTheDocument()
  await userEvent.type(screen.getByLabelText('Entry price'), '100')
  await userEvent.type(screen.getByLabelText('Stop price'), '90')
  await userEvent.type(screen.getByLabelText('Target price'), '130')
  await userEvent.click(screen.getByRole('button', { name: 'Check risk / reward' }))
  expect(await screen.findByText('3×')).toBeInTheDocument()
})

test('restores a session and renders a deep calendar link', async () => {
  server.use(...authenticatedHandlers())
  renderApp('/calendar/2026/07')
  expect(await screen.findByRole('heading', { name: 'Calendar' })).toBeInTheDocument()
  expect(window.location.pathname).toBe('/calendar/2026/07')
})

test('settings exposes independent scheme and accent switches', async () => {
  const writes: Array<{ appearance: string; accentTheme: string }> = []
  server.use(...authenticatedHandlers(),
    http.get('/api/app/settings', () => HttpResponse.json({
      email: 'owner@example.com', displayName: 'Owner', timezone: 'Asia/Taipei', journalDayRollover: '00:00',
      baseCurrency: 'USD', appearance: 'system', accentTheme: 'green', locale: 'en', updatedAt: '2026-07-16T00:00:00Z',
    })),
    http.put('/api/app/settings', async ({ request }) => {
      const body = await request.json() as { displayName: string; timezone: string; journalDayRollover: string; baseCurrency: string; appearance: string; accentTheme: string; locale: string }
      writes.push(body)
      return HttpResponse.json({ email: 'owner@example.com', ...body, updatedAt: '2026-07-16T00:00:00Z' })
    }))
  renderApp('/settings')
  const appearanceHeading = await screen.findByRole('heading', { name: 'Appearance' })
  const appearanceSection = appearanceHeading.closest('section')!
  const controls = within(appearanceSection).getByRole('group', { name: 'Theme controls' })
  expect(within(controls).queryAllByRole('radio')).toHaveLength(0)
  expect(within(controls).getAllByRole('switch')).toHaveLength(2)

  await userEvent.click(within(controls).getByRole('switch', { name: 'Light or dark mode' }))
  await userEvent.click(within(controls).getByRole('switch', { name: 'Green or red accent' }))

  await waitFor(() => expect(writes.at(-1)).toMatchObject({ appearance: 'light', accentTheme: 'red' }))
  expect(document.documentElement).toHaveAttribute('data-theme', 'light')
  expect(document.documentElement).toHaveAttribute('data-accent', 'red')
})

test('settings exposes the Journal Day rollover', async () => {
  server.use(...authenticatedHandlers())
  server.use(http.get('/api/app/settings', () => HttpResponse.json({
    email: 'owner@example.com', displayName: 'Owner', timezone: 'Asia/Taipei', journalDayRollover: '00:00',
    baseCurrency: 'USD', appearance: 'system', accentTheme: 'green', locale: 'en', updatedAt: '2026-07-16T00:00:00Z',
  })))

  renderApp('/settings')
  const rolloverLabel = await screen.findByText('Journal Day rollover')
  const rollover = rolloverLabel.closest('label')?.querySelector('input')
  if (!rollover) throw new Error('rollover input missing')
  expect(rollover).toHaveValue('00:00')
})

test('mobile Today captures and edits observation updates with an honesty reminder', async () => {
  Object.defineProperty(window, 'innerWidth', { configurable: true, value: 390 })
  window.dispatchEvent(new Event('resize'))
  let savedWrite: ObservationUpdateWrite | null = null
  let updates: ObservationUpdateResponse[] = [{
    id: 'update-1', content: 'Opening breadth was weak', recordedAt: '2026-07-16T13:30:00Z', updatedAt: '2026-07-16T13:30:00Z',
    signal: null, interpretation: null, mentalState: null, tags: [], primarySubject: null, relatedSubjects: [], evidence: null,
  }]
  server.use(...authenticatedHandlers())
  server.use(
    http.get('/api/app/market-observations/today', () => HttpResponse.json({
      id: 'observation-1', journalDay: '2026-07-16', timezone: 'Asia/Taipei', rollover: '00:00', updates,
    })),
    http.get('/api/app/market/symbols', () => HttpResponse.json({
      contractVersion: 1,
      items: [{ instrumentId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', symbol: 'AAPL', name: 'Apple Inc.', exchange: 'NASDAQ', currency: 'USD', timezone: 'America/New_York' }],
    })),
    http.post('/api/app/quick-observations', async ({ request }) => {
      const body = await request.json() as { content: string }
      updates = [...updates, {
        id: 'update-2', content: body.content, recordedAt: '2026-07-16T14:00:00Z', updatedAt: '2026-07-16T14:00:00Z',
        signal: null, interpretation: null, mentalState: null, tags: [], primarySubject: null, relatedSubjects: [], evidence: null,
      }]
      return HttpResponse.json({ marketObservationId: 'observation-1', observationUpdateId: 'update-2', journalDay: '2026-07-16', recordedAt: '2026-07-16T14:00:00Z', appended: true })
    }),
    http.put('/api/app/observation-updates/:id', async ({ request }) => {
      const body = await request.json() as ObservationUpdateWrite
      savedWrite = body
      const responseSubject = (subject: NonNullable<ObservationUpdateWrite['primarySubject']>) => ({
        type: subject.type, name: subject.name ?? null, instrumentId: subject.instrumentId ?? null, market: subject.market ?? null,
        symbol: subject.symbol ?? null, displayName: subject.displayName ?? null, dailyCloseAvailable: subject.market === 'US',
      })
      const primarySubject = body.primarySubject ? responseSubject(body.primarySubject) : null
      const relatedSubjects = (body.relatedSubjects ?? []).map(responseSubject)
      updates = updates.map(update => update.id === 'update-1' ? {
        ...update, content: body.content, signal: body.signal ?? null, interpretation: body.interpretation ?? null,
        mentalState: body.mentalState ?? null, tags: body.tags ?? [], primarySubject, relatedSubjects,
        evidence: body.evidence ? { url: body.evidence.url, title: body.evidence.title ?? null, quote: body.evidence.quote ?? null } : null,
        updatedAt: '2026-07-16T15:00:00Z',
      } : update)
      return HttpResponse.json({ ...updates[0], honestyReminderRequired: true })
    }))

  renderApp('/today')
  expect(await screen.findByText('Opening breadth was weak')).toBeInTheDocument()
  expect(screen.getByText('21:30')).toBeInTheDocument()
  await userEvent.type(screen.getByLabelText('What did you notice today?'), 'Buyers returned near the close')
  await userEvent.click(screen.getByRole('button', { name: 'Save observation' }))
  expect(await screen.findByText('Buyers returned near the close')).toBeInTheDocument()

  await userEvent.click(screen.getAllByRole('button', { name: 'Edit observation' })[0])
  expect(screen.getByText(/Editing the past can weaken your self-review/)).toBeInTheDocument()
  const editor = screen.getByLabelText('Edit observation text')
  await userEvent.clear(editor)
  await userEvent.type(editor, 'Opening breadth improved')
  await userEvent.type(screen.getByLabelText('Signal'), 'Advancers led decliners')
  await userEvent.type(screen.getByLabelText('Interpretation'), 'Risk appetite may be returning')
  await userEvent.type(screen.getByLabelText('Mental state'), 'patient')
  await userEvent.type(screen.getByLabelText('Tags'), 'breadth, closing session')
  await userEvent.selectOptions(screen.getByLabelText('Primary subject type'), 'instrument')
  await userEvent.selectOptions(await screen.findByLabelText('US Instrument'), 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa')
  await userEvent.click(screen.getByRole('button', { name: 'Add related subject' }))
  await userEvent.selectOptions(screen.getByLabelText('Related subject type'), 'instrument')
  const markets = screen.getAllByLabelText('Market')
  await userEvent.clear(markets[1])
  await userEvent.type(markets[1], 'HK')
  await userEvent.type(screen.getByLabelText('Symbol'), '0700')
  await userEvent.type(screen.getByLabelText('Display name'), 'Tencent')
  await userEvent.type(screen.getByLabelText('Evidence URL'), 'https://example.com/market')
  await userEvent.type(screen.getByLabelText('Your excerpt'), 'Advancers led decliners.')
  await userEvent.click(screen.getByRole('button', { name: 'Save edit' }))
  expect(savedWrite).toMatchObject({ primarySubject: { type: 'instrument', instrumentId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa' }, relatedSubjects: [{ type: 'instrument', market: 'HK', symbol: '0700' }] })
  const updatedItem = (await screen.findByText('Opening breadth improved')).closest('li')!
  expect(updatedItem).toHaveTextContent('Advancers led decliners')
  expect(updatedItem).toHaveTextContent('Primary subject: AAPL · Apple Inc.')
  expect(updatedItem).toHaveTextContent('Related subject: 0700 · Tencent · Daily Close unavailable')
  expect(updatedItem).toHaveTextContent('Mental state: patient')
  expect(updatedItem).toHaveTextContent('Your excerpt: Advancers led decliners.')
  Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1024 })
  window.dispatchEvent(new Event('resize'))
})

test('Today enrichment controls are available in Traditional Chinese', async () => {
  server.use(...authenticatedHandlers())
  server.use(
    http.get('/api/app/bootstrap', () => HttpResponse.json({ ...bootstrap, locale: 'zh-Hant' })),
    http.get('/api/app/market/symbols', () => HttpResponse.json({ contractVersion: 1, items: [] })),
    http.get('/api/app/market-observations/today', () => HttpResponse.json({
      id: 'observation-1', journalDay: '2026-07-16', timezone: 'Asia/Taipei', rollover: '00:00',
      updates: [{ id: 'update-1', content: '開市廣度偏弱', recordedAt: '2026-07-16T13:30:00Z', updatedAt: '2026-07-16T13:30:00Z', signal: null, interpretation: null, mentalState: null, tags: [], primarySubject: null, relatedSubjects: [], evidence: null }],
    }))
  )

  renderApp('/today')
  await userEvent.click(await screen.findByRole('button', { name: '編輯市場觀察' }))
  expect(screen.getByLabelText('訊號')).toBeInTheDocument()
  expect(screen.getByLabelText('詮釋')).toBeInTheDocument()
  expect(screen.getByLabelText('主要觀察主體類型')).toBeInTheDocument()
  expect(screen.getByLabelText('證據網址')).toBeInTheDocument()
})

test('mobile Today opens paginated observation history and an Instrument timeline', async () => {
  Object.defineProperty(window, 'innerWidth', { configurable: true, value: 390 })
  window.dispatchEvent(new Event('resize'))
  const requests: URL[] = []
  server.use(...authenticatedHandlers())
  server.use(
    http.get('/api/app/market/symbols', () => HttpResponse.json({ contractVersion: 1, items: [] })),
    http.get('/api/app/market-observations', ({ request }) => {
      const url = new URL(request.url)
      requests.push(url)
      const cursor = url.searchParams.get('cursor')
      const instrumentId = url.searchParams.get('instrumentId')
      const first = {
        marketObservationId: 'observation-1', journalDay: '2026-07-16', authorId: bootstrap.currentUser.id,
        update: { id: 'update-1', content: 'Semiconductor breadth improved', recordedAt: '2026-07-16T13:30:00Z', updatedAt: '2026-07-16T13:30:00Z', signal: 'Advancers led decliners', interpretation: null, mentalState: null, tags: ['breadth'], primarySubject: { type: 'instrument', name: null, instrumentId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', market: 'US', symbol: 'AAPL', displayName: 'Apple Inc.', dailyCloseAvailable: true }, relatedSubjects: [], evidence: null },
      }
      const second = {
        marketObservationId: 'observation-2', journalDay: '2026-07-15', authorId: bootstrap.currentUser.id,
        update: { ...first.update, id: 'update-2', content: 'Dollar strengthened', primarySubject: { type: 'broad_market', name: 'US macro', instrumentId: null, market: null, symbol: null, displayName: null, dailyCloseAvailable: false } },
      }
      if (instrumentId) return HttpResponse.json({ items: [first], nextCursor: null })
      return cursor ? HttpResponse.json({ items: [second], nextCursor: null }) : HttpResponse.json({ items: [first], nextCursor: 'next-page' })
    }))

  renderApp('/today')
  await userEvent.click(await screen.findByRole('link', { name: 'All observations' }))
  expect(await screen.findByRole('heading', { name: 'All observations' })).toBeInTheDocument()
  expect(screen.getByText('Semiconductor breadth improved')).toBeInTheDocument()
  expect(screen.getByText(/currently retained/i)).toBeInTheDocument()
  await userEvent.click(screen.getByRole('button', { name: 'Load more' }))
  expect(await screen.findByText('Dollar strengthened')).toBeInTheDocument()
  await userEvent.click(screen.getByRole('link', { name: 'AAPL · Apple Inc.' }))
  await waitFor(() => expect(requests.at(-1)?.searchParams.get('instrumentId')).toBe('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'))
  expect(window.location.pathname).toBe('/today/observations')
  Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1024 })
  window.dispatchEvent(new Event('resize'))
})

test('observation history keeps loaded results when the next page fails', async () => {
  server.use(...authenticatedHandlers())
  server.use(http.get('/api/app/market-observations', ({ request }) => {
    if (new URL(request.url).searchParams.get('cursor')) return new HttpResponse(null, { status: 503 })
    return HttpResponse.json({ items: [{
      marketObservationId: 'observation-1', journalDay: '2026-07-16', authorId: bootstrap.currentUser.id,
      update: { id: 'update-1', content: 'Retained first page', recordedAt: '2026-07-16T13:30:00Z', updatedAt: '2026-07-16T13:30:00Z', signal: null, interpretation: null, mentalState: null, tags: [], primarySubject: null, relatedSubjects: [], evidence: null },
    }], nextCursor: 'next-page' })
  }))
  renderApp('/today/observations')
  expect(await screen.findByText('Retained first page')).toBeInTheDocument()
  await userEvent.click(screen.getByRole('button', { name: 'Load more' }))
  expect(await screen.findByText('Could not load more observations. Try again.')).toBeInTheDocument()
  expect(screen.getByText('Retained first page')).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument()
})

test('observation history has Traditional Chinese empty and error states', async () => {
  server.use(...authenticatedHandlers())
  server.use(
    http.get('/api/app/bootstrap', () => HttpResponse.json({ ...bootstrap, locale: 'zh-Hant' })),
    http.get('/api/app/market-observations', () => HttpResponse.json({ items: [], nextCursor: null })),
  )
  renderApp('/today/observations')
  expect(await screen.findByRole('heading', { name: '所有市場觀察' })).toBeInTheDocument()
  expect(await screen.findByText('找不到市場觀察')).toBeInTheDocument()
})

test('observation history shows a retry state when search fails', async () => {
  server.use(...authenticatedHandlers())
  server.use(http.get('/api/app/market-observations', () => new HttpResponse(null, { status: 503 })))
  renderApp('/today/observations')
  expect(await screen.findByText('Couldn’t reach the cockpit.')).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument()
})

test('Calendar shows Market Observations without the old Performance surface', async () => {
  server.use(...authenticatedHandlers())
  server.use(http.get('/api/app/calendar', () => HttpResponse.json({
    year: 2026, month: 7, days: [
      { date: '2026-07-01', marketObservationId: 'observation-1', updateCount: 2, readyForReviewCount: null },
      ...Array.from({ length: 30 }, (_, index) => ({ date: `2026-07-${String(index + 2).padStart(2, '0')}`, marketObservationId: null, updateCount: 0, readyForReviewCount: null })),
    ],
  })))
  renderApp('/calendar/2026/07?day=2026-07-01')
  expect(await screen.findByText('Market Observations by Journal Day')).toBeInTheDocument()
  expect(await screen.findByText('2 observation updates')).toBeInTheDocument()
  expect(screen.getByRole('link', { name: 'Open this day’s observations' })).toHaveAttribute('href', '/today/observations?from=2026-07-01&to=2026-07-01')
  expect(screen.queryByText('Net P/L')).not.toBeInTheDocument()
  expect(screen.queryByLabelText('P/L amount')).not.toBeInTheDocument()
})

test('quick observation remains available when dashboard composition fails', async () => {
  server.use(...authenticatedHandlers())
  server.use(http.get('/api/app/dashboard', () => new HttpResponse(null, { status: 503 })))
  renderApp('/today')
  expect(await screen.findByLabelText('What did you notice today?')).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Save observation' })).toBeDisabled()
  expect(await screen.findByText('Couldn’t reach the cockpit.')).toBeInTheDocument()
})

test('loads a diary and its transactions from a direct detail link', async () => {
  server.use(...authenticatedHandlers(),
    http.get('/api/app/diaries/:id', () => HttpResponse.json({ id: 'diary-1', localDate: '2026-07-16', title: 'Direct entry', content: 'Notes', createdAt: '2026-07-16T00:00:00Z', updatedAt: '2026-07-16T00:00:00Z', tags: [] })),
    http.get('/api/app/diaries/:id/transactions', () => HttpResponse.json({ items: [], nextCursor: null })),
    http.get('/api/app/diaries/:id/review', () => HttpResponse.json({ diaryId: 'diary-1', thesis: 'Demand remains strong', plannedAction: null, actualAction: null, emotion: 'calm', disciplineScore: 4, executionScore: null, processAssessment: 'good', mistakeTags: [], lesson: null, nextAction: null, createdAt: '2026-07-16T00:00:00Z', updatedAt: '2026-07-16T00:00:00Z' })))
  renderApp('/diary/diary-1')
  expect(await screen.findByRole('heading', { name: 'Direct entry' })).toBeInTheDocument()
  expect(await screen.findByText('No trades logged.')).toBeInTheDocument()
  await userEvent.click(screen.getByText('Decision review'))
  expect(await screen.findByDisplayValue('Demand remains strong')).toBeInTheDocument()
})

test('decision review hash expands and focuses the review after loading', async () => {
  server.use(...authenticatedHandlers(),
    http.get('/api/app/diaries/:id', () => HttpResponse.json({ id: 'diary-1', localDate: '2026-07-16', title: 'Direct entry', content: 'Notes', createdAt: '2026-07-16T00:00:00Z', updatedAt: '2026-07-16T00:00:00Z', tags: [] })),
    http.get('/api/app/diaries/:id/transactions', () => HttpResponse.json({ items: [], nextCursor: null })),
    http.get('/api/app/diaries/:id/review', () => new HttpResponse(null, { status: 404 })))
  renderApp('/diary/diary-1#decision-review')
  const heading = await screen.findByText('Decision review')
  await waitFor(() => expect(heading.closest('details')).toHaveAttribute('open'))
  expect(document.activeElement).toBe(heading)
})

test('calendar day query selects the exact valid route-month date', async () => {
  server.use(...authenticatedHandlers())
  renderApp('/calendar/2026/07?day=2026-07-09')
  expect(await screen.findByRole('heading', { name: '2026-07-09' })).toBeInTheDocument()
})

test('missing diary review shows an empty state', async () => {
  server.use(...authenticatedHandlers(),
    http.get('/api/app/diaries/:id', () => HttpResponse.json({ id: 'diary-1', localDate: '2026-07-16', title: 'Direct entry', content: 'Notes', createdAt: '2026-07-16T00:00:00Z', updatedAt: '2026-07-16T00:00:00Z', tags: [] })),
    http.get('/api/app/diaries/:id/transactions', () => HttpResponse.json({ items: [], nextCursor: null })),
    http.get('/api/app/diaries/:id/review', () => new HttpResponse(null, { status: 404 })))
  renderApp('/diary/diary-1')
  await userEvent.click(await screen.findByText('Decision review'))
  expect(await screen.findByText('No structured review yet')).toBeInTheDocument()
})

test('saves an optional structured review', async () => {
  let savedThesis: string | null = null
  server.use(...authenticatedHandlers(),
    http.get('/api/app/diaries/:id', () => HttpResponse.json({ id: 'diary-1', localDate: '2026-07-16', title: 'Direct entry', content: 'Notes', createdAt: '2026-07-16T00:00:00Z', updatedAt: '2026-07-16T00:00:00Z', tags: [] })),
    http.get('/api/app/diaries/:id/transactions', () => HttpResponse.json({ items: [], nextCursor: null })),
    http.get('/api/app/diaries/:id/review', () => new HttpResponse(null, { status: 404 })),
    http.put('/api/app/diaries/:id/review', async ({ request }) => {
      savedThesis = ((await request.json()) as { thesis: string | null }).thesis
      return HttpResponse.json({ diaryId: 'diary-1', thesis: savedThesis, plannedAction: null, actualAction: null, emotion: null, disciplineScore: null, executionScore: null, processAssessment: null, mistakeTags: [], lesson: null, nextAction: null, createdAt: '2026-07-16T00:00:00Z', updatedAt: '2026-07-16T00:00:00Z' })
    }))
  renderApp('/diary/diary-1')
  await userEvent.click(await screen.findByText('Decision review'))
  await userEvent.type(screen.getByLabelText('Thesis'), 'Follow the original plan')
  await userEvent.click(screen.getByRole('button', { name: 'Save review' }))
  await waitFor(() => expect(savedThesis).toBe('Follow the original plan'))
})

test('deletes a structured review only after confirmation', async () => {
  let deleted = false
  server.use(...authenticatedHandlers(),
    http.get('/api/app/diaries/:id', () => HttpResponse.json({ id: 'diary-1', localDate: '2026-07-16', title: 'Direct entry', content: 'Notes', createdAt: '2026-07-16T00:00:00Z', updatedAt: '2026-07-16T00:00:00Z', tags: [] })),
    http.get('/api/app/diaries/:id/transactions', () => HttpResponse.json({ items: [], nextCursor: null })),
    http.get('/api/app/diaries/:id/review', () => HttpResponse.json({ diaryId: 'diary-1', thesis: null, plannedAction: null, actualAction: null, emotion: null, disciplineScore: null, executionScore: null, processAssessment: null, mistakeTags: [], lesson: null, nextAction: null, createdAt: '2026-07-16T00:00:00Z', updatedAt: '2026-07-16T00:00:00Z' })),
    http.delete('/api/app/diaries/:id/review', () => { deleted = true; return new HttpResponse(null, { status: 204 }) }))
  renderApp('/diary/diary-1')
  await userEvent.click(await screen.findByText('Decision review'))
  await userEvent.click(await screen.findByRole('button', { name: 'Delete review' }))
  expect(deleted).toBe(false)
  const dialog = await screen.findByRole('alertdialog')
  await userEvent.click(within(dialog).getByRole('button', { name: 'Delete' }))
  await waitFor(() => expect(deleted).toBe(true))
})

test('review summary empty state does not show fake zero averages', async () => {
  server.use(...authenticatedHandlers())
  renderApp('/diary')
  expect(await screen.findByText('No structured reviews yet')).toBeInTheDocument()
  expect(screen.queryByText('0.0')).not.toBeInTheDocument()
})

test('authenticated bootstrap local date drives the review summary range', async () => {
  let requestedRange = ''
  server.use(...authenticatedHandlers())
  server.use(
    http.get('/api/app/bootstrap', () => HttpResponse.json({ ...bootstrap, currentLocalDate: '2030-03-01' })),
    http.get('/api/app/diary-review-summary', ({ request }) => {
      const url = new URL(request.url)
      requestedRange = `${url.searchParams.get('from')}:${url.searchParams.get('to')}`
      return HttpResponse.json({ reviewedCount: 0, averageDisciplineScore: null, averageExecutionScore: null, emotionCounts: {}, processAssessmentCounts: {}, topMistakeTags: [] })
    }))
  renderApp('/diary')
  await screen.findByText('No structured reviews yet')
  expect(requestedRange).toBe('2030-01-31:2030-03-01')
})

test('calendar month navigation updates the URL', async () => {
  server.use(...authenticatedHandlers())
  renderApp('/calendar/2026/07')
  await userEvent.click(await screen.findByRole('button', { name: 'Next month' }))
  expect(window.location.pathname).toBe('/calendar/2026/08')
})

test('browser navigation uses route links', async () => {
  server.use(...authenticatedHandlers())
  renderApp('/today')
  await screen.findByText('Recent reflections')
  await userEvent.click(screen.getAllByRole('link', { name: /Diary/ })[0])
  expect(window.location.pathname).toBe('/diary')
  expect(await screen.findByRole('heading', { name: 'Diary' })).toBeInTheDocument()
})

test('anonymous users can open the register page from login', async () => {
  server.use(http.post('/api/auth/refresh', () => new HttpResponse(null, { status: 401 })))
  renderApp('/login')
  await userEvent.click(await screen.findByRole('link', { name: 'Create one' }))
  expect(window.location.pathname).toBe('/register')
  expect(await screen.findByRole('heading', { name: 'Create your cockpit.' })).toBeInTheDocument()
  await userEvent.click(screen.getByRole('link', { name: 'Sign in' }))
  expect(window.location.pathname).toBe('/login')
})

type RegistrationPayload = { email: string; password: string; displayName: string; timezone: string; baseCurrency: string }
type LoginPayload = { email: string; password: string }

test('public registration creates an account and signs in', async () => {
  const observed: { registered?: RegistrationPayload; loggedIn?: LoginPayload } = {}
  server.use(
    http.post('/api/auth/refresh', () => new HttpResponse(null, { status: 401 })),
    http.get('/api/app/bootstrap', () => HttpResponse.json(bootstrap)),
    http.get('/api/app/dashboard', () => HttpResponse.json({
      localDate: '2026-07-16', diary: { writtenToday: false, count: 0 }, performance: null,
      pendingAlerts: null, discipline: null, recentDiaries: [], capabilities: { alerts: 'unavailable', discipline: 'empty' },
    })),
    http.post('/api/auth/register', async ({ request }) => {
      const payload = await request.json() as RegistrationPayload
      observed.registered = payload
      return HttpResponse.json({ id: '22222222-2222-2222-2222-222222222222', email: payload.email, displayName: payload.displayName, timezone: payload.timezone, baseCurrency: payload.baseCurrency }, { status: 201 })
    }),
    http.post('/api/auth/login', async ({ request }) => {
      observed.loggedIn = await request.json() as LoginPayload
      return HttpResponse.json({ accessToken: 'memory-only-token', expiresAt: '2026-07-16T12:00:00Z' })
    }))
  renderApp('/register')
  await userEvent.type(await screen.findByLabelText('Name'), 'New Trader')
  await userEvent.type(screen.getByLabelText('Email'), 'new@example.com')
  await userEvent.type(screen.getByLabelText(/Password/), 'correct horse battery staple')
  await userEvent.click(screen.getByRole('button', { name: 'Create account' }))
  await screen.findByText('Recent reflections')
  expect(window.location.pathname).toBe('/today')
  const registered = observed.registered
  if (!registered) throw new Error('registration request was not sent')
  expect(registered).toMatchObject({ email: 'new@example.com', password: 'correct horse battery staple', displayName: 'New Trader', baseCurrency: 'USD' })
  expect(registered.timezone).toBeTruthy()
  expect(observed.loggedIn).toEqual({ email: 'new@example.com', password: 'correct horse battery staple' })
})

test('public registration explains duplicate and unavailable states', async () => {
  server.use(http.post('/api/auth/refresh', () => new HttpResponse(null, { status: 401 })))
  server.use(http.post('/api/auth/register', () => new HttpResponse(null, { status: 409 })))
  renderApp('/register')
  await userEvent.type(await screen.findByLabelText('Name'), 'Existing Trader')
  await userEvent.type(screen.getByLabelText('Email'), 'existing@example.com')
  await userEvent.type(screen.getByLabelText(/Password/), 'correct horse battery staple')
  await userEvent.click(screen.getByRole('button', { name: 'Create account' }))
  expect(await screen.findByText('Unable to create this account. Try signing in if you may already be registered.')).toBeInTheDocument()

  server.use(http.post('/api/auth/register', () => new HttpResponse(null, { status: 404 })))
  await userEvent.click(screen.getByRole('button', { name: 'Create account' }))
  expect(await screen.findByText('Registration is not available on this deployment.')).toBeInTheDocument()
})

test('login failure after successful registration offers a sign-in path', async () => {
  server.use(
    http.post('/api/auth/refresh', () => new HttpResponse(null, { status: 401 })),
    http.post('/api/auth/register', () => HttpResponse.json({
      id: '33333333-3333-3333-3333-333333333333',
      email: 'created@example.com',
      displayName: 'Created',
      timezone: 'UTC',
      baseCurrency: 'USD',
    }, { status: 201 })),
    http.post('/api/auth/login', () => new HttpResponse(null, { status: 401 })),
  )
  renderApp('/register')
  await userEvent.type(await screen.findByLabelText('Name'), 'Created Trader')
  await userEvent.type(screen.getByLabelText('Email'), 'created@example.com')
  await userEvent.type(screen.getByLabelText(/Password/), 'correct horse battery staple')
  await userEvent.click(screen.getByRole('button', { name: 'Create account' }))
  expect(await screen.findByText('Account created. Please sign in.')).toBeInTheDocument()
  await userEvent.click(screen.getByRole('link', { name: 'Continue to sign in' }))
  expect(window.location.pathname).toBe('/login')
})

test('anonymous sessions redirect to login', async () => {
  server.use(http.post('/api/auth/refresh', () => new HttpResponse(null, { status: 401 })))
  renderApp('/today')
  expect(await screen.findByRole('heading', { name: 'Your decisions, remembered.' })).toBeInTheDocument()
  expect(window.location.pathname).toBe('/login')
})

test('unknown routes show not found', async () => {
  server.use(...authenticatedHandlers())
  renderApp('/not-a-route')
  expect(await screen.findByText('Page not found.')).toBeInTheDocument()
})

test('logout clears protected query cache', async () => {
  server.use(...authenticatedHandlers())
  const client = renderApp('/today')
  client.setQueryData(['diaries'], { items: [{ id: 'private' }] })
  await screen.findByText('Recent reflections')
  await userEvent.click(screen.getAllByRole('button', { name: 'Sign out' })[0])
  await waitFor(() => expect(window.location.pathname).toBe('/login'))
  expect(client.getQueryData(['diaries'])).toBeUndefined()
})
