import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { BrowserRouter } from 'react-router-dom'
import { expect, test } from 'vitest'
import App from '../App'
import { AuthProvider } from '../auth/AuthProvider'
import { I18nProvider } from '../i18n'
import { server } from './setup'


const instrumentId = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
const updateId = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb'
const expectationId = 'cccccccc-cccc-cccc-cccc-cccccccccccc'
const userId = '11111111-1111-1111-1111-111111111111'

const bootstrap = {
  currentUser: { id: userId, email: 'owner@example.com', displayName: 'Owner' },
  timezone: 'Asia/Taipei', journalDayRollover: '00:00', baseCurrency: 'USD',
  appearance: 'system', accentTheme: 'green', locale: 'en', role: 'user', accountType: 'human',
  currentJournalDay: '2026-07-16', availableProductAreas: ['today', 'review', 'watchlist', 'calendar', 'tools', 'settings'],
}

function handlers() {
  return [
    http.post('/api/auth/refresh', () => HttpResponse.json({ accessToken: 'memory-only-token', expiresAt: '2026-07-16T12:00:00Z' })),
    http.get('/api/app/bootstrap', () => HttpResponse.json(bootstrap)),
    http.get('/api/app/market-observations/today', () => HttpResponse.json({
      id: 'dddddddd-dddd-dddd-dddd-dddddddddddd', journalDay: '2026-07-16', timezone: 'Asia/Taipei', rollover: '00:00',
      updates: [{
        id: updateId, content: 'Blackwell demand strengthened.\nVolume confirmed the move.', recordedAt: '2026-07-16T10:00:00Z', updatedAt: '2026-07-16T10:00:00Z',
        signal: null, interpretation: null, mentalState: null, tags: [],
        primarySubject: { type: 'instrument', name: null, instrumentId, market: 'US', symbol: 'NVDA', displayName: 'NVIDIA Corporation', dailyCloseAvailable: false, dailyCloseStatus: 'unsupported', dailyClose: null },
        relatedSubjects: [], evidence: null,
      }],
    })),
    http.get('/api/app/market-observations', () => HttpResponse.json({ items: [], nextCursor: null })),
    http.get('/api/app/market/symbols', () => HttpResponse.json({ items: [{ instrumentId, symbol: 'NVDA', name: 'NVIDIA Corporation', exchange: 'NASDAQ', currency: 'USD', timezone: 'America/New_York' }] })),
    http.get('/api/app/expectations', () => HttpResponse.json({ items: [{
      id: expectationId, observationUpdateId: updateId, marketObservationId: 'dddddddd-dddd-dddd-dddd-dddddddddddd',
      expectedBehavior: 'Demand remains firm', deadline: '2026-07-20T12:00:00Z', invalidationCondition: 'Demand reverses', confidence: 'medium', market: 'US',
      invalidatedAt: null, readiness: 'ready_for_review', deadlineElapsed: true, createdAt: '2026-07-16T10:30:00Z', updatedAt: '2026-07-16T10:30:00Z',
    }] })),
    http.get('/api/app/observation-updates/:updateId/action-decisions', () => HttpResponse.json({ items: [] })),
    http.get('/api/app/discipline-principles/today', () => new HttpResponse(null, { status: 404 })),
    http.get('/api/app/watchlist', () => HttpResponse.json({ items: [] })),
  ]
}

function renderApp(path: string) {
  window.history.replaceState({}, '', path)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(<QueryClientProvider client={client}><BrowserRouter><AuthProvider><I18nProvider><App /></I18nProvider></AuthProvider></BrowserRouter></QueryClientProvider>)
}

test('an instrument observation can continue into Watchlist and a scoped new observation', async () => {
  let watchlistBody: unknown
  server.use(...handlers(), http.post('/api/app/watchlist/:instrumentId', async ({ request }) => {
    watchlistBody = await request.json()
    return new HttpResponse(null, { status: 204 })
  }))
  renderApp('/today')

  const add = await screen.findByRole('button', { name: 'Add to Watchlist' })
  await userEvent.click(add)
  await waitFor(() => expect(watchlistBody).toEqual({ note: 'Blackwell demand strengthened.\nVolume confirmed the move.' }))
  expect(screen.getByRole('link', { name: 'Observe again' })).toHaveAttribute('href', `/today?instrumentId=${instrumentId}`)
  expect(screen.getAllByRole('link', { name: 'Compare' })[0]).toHaveAttribute('href', `/review?from=2026-07-16&to=2026-07-20&instrumentId=${instrumentId}`)
})

test('a Watchlist new-observation route keeps instrument context and source label', async () => {
  let quickBody: unknown
  server.use(...handlers(), http.post('/api/app/quick-observations', async ({ request }) => {
    quickBody = await request.json()
    return HttpResponse.json({ marketObservationId: 'dddddddd-dddd-dddd-dddd-dddddddddddd', observationUpdateId: updateId, journalDay: '2026-07-16', recordedAt: '2026-07-16T10:00:00Z', appended: true })
  }))
  renderApp(`/today?instrumentId=${instrumentId}`)
  expect(await screen.findByText('New observation context: NVDA · NVIDIA Corporation.')).toBeInTheDocument()
  const composer = screen.getByLabelText('What did you notice today?')
  await userEvent.type(composer, 'Follow-up evidence')
  await userEvent.click(screen.getByRole('button', { name: 'Save observation' }))
  await waitFor(() => expect(quickBody).toEqual({ content: 'Follow-up evidence', sourceLabel: 'instrument:NVDA · NVIDIA Corporation' }))
})
