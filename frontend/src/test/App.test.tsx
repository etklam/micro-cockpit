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

const bootstrap = {
  currentUser: { id: '11111111-1111-1111-1111-111111111111', email: 'owner@example.com', displayName: 'Owner' },
  timezone: 'Asia/Taipei',
  journalDayRollover: '00:00',
  baseCurrency: 'USD',
  appearance: 'system',
  accentTheme: 'green',
  locale: 'en',
  role: 'user',
  accountType: 'human',
  currentJournalDay: '2026-07-16',
  availableProductAreas: ['today', 'review', 'watchlist', 'calendar', 'tools', 'settings'],
}

function authenticatedHandlers() {
  return [
    http.post('/api/auth/refresh', () =>
      HttpResponse.json({ accessToken: 'memory-only-token', expiresAt: '2026-07-16T12:00:00Z' })),
    http.get('/api/app/bootstrap', () => HttpResponse.json(bootstrap)),
    http.get('/api/app/market-observations/today', () => new HttpResponse(null, { status: 404 })),
    http.get('/api/app/market-observations', () => HttpResponse.json({ items: [], nextCursor: null })),
    http.get('/api/app/expectations', () => HttpResponse.json({ items: [] })),
    http.get('/api/app/discipline-principles/today', () => new HttpResponse(null, { status: 404 })),
    http.get('/api/app/market/symbols', () => HttpResponse.json({ items: [] })),
    http.get('/api/app/agents', () => HttpResponse.json({ items: [
      { userId: '22222222-2222-2222-2222-222222222222', displayName: 'Atlas', timezone: 'UTC', baseCurrency: 'USD', keyId: null, scopes: [], tokenCreatedAt: null, lastUsedAt: null, lastSuccessfulRequestAt: null },
    ] })),
    http.get('/api/app/discipline-principles', () => HttpResponse.json({ items: [] })),
    http.get('/api/app/pattern-review', () => HttpResponse.json({ range: 'weekly', from: '2026-07-13', to: '2026-07-19', reviewedExpectationCount: 0, labels: [] })),
    http.get('/api/app/calendar', ({ request }) => {
      const url = new URL(request.url)
      return HttpResponse.json({
        year: Number(url.searchParams.get('year')),
        month: Number(url.searchParams.get('month')),
        days: [],
      })
    }),
    http.post('/api/auth/logout', () => new HttpResponse(null, { status: 204 })),
  ]
}

function renderApp(path: string) {
  window.history.replaceState({}, '', path)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <BrowserRouter>
        <AuthProvider>
          <I18nProvider><App /></I18nProvider>
        </AuthProvider>
      </BrowserRouter>
    </QueryClientProvider>,
  )
  return client
}

test('public landing and calculators remain available without signing in', async () => {
  server.use(http.post('/api/auth/refresh', () => new HttpResponse(null, { status: 401 })))
  renderApp('/')
  expect(await screen.findByRole('heading', { name: 'A cockpit for developing a market view.' })).toBeInTheDocument()
  expect(screen.getByRole('link', { name: 'Try tools free' })).toHaveAttribute('href', '/tools?tool=position-sizing')
})

test('authenticated shell exposes exactly the six cutover destinations', async () => {
  server.use(...authenticatedHandlers())
  renderApp('/today')
  await screen.findByLabelText('What did you notice today?')
  const primary = screen.getByRole('navigation', { name: 'Primary sections' })
  expect(primary.querySelectorAll('a')).toHaveLength(6)
  for (const name of ['Today', 'Review', 'Watchlist', 'Calendar', 'Tools', 'Settings'])
    expect(screen.getAllByRole('link', { name }).length).toBeGreaterThan(0)
})

test('quick observation is the Today golden-path entry point', async () => {
  let content = ''
  server.use(...authenticatedHandlers())
  server.use(http.post('/api/app/quick-observations', async ({ request }) => {
    content = ((await request.json()) as { content: string }).content
    return HttpResponse.json({})
  }))
  renderApp('/today')
  await userEvent.type(await screen.findByLabelText('What did you notice today?'), 'Breadth weakened into the close.')
  await userEvent.click(screen.getByRole('button', { name: 'Save observation' }))
  await waitFor(() => expect(content).toBe('Breadth weakened into the close.'))
})

test('Today shows recent real Observation Updates and honest unavailable controls', async () => {
  server.use(...authenticatedHandlers())
  server.use(http.get('/api/app/market-observations', () => HttpResponse.json({
    items: [{
      marketObservationId: '77777777-7777-7777-7777-777777777777',
      journalDay: '2026-07-15',
      authorId: bootstrap.currentUser.id,
      update: {
        id: '88888888-8888-8888-8888-888888888888',
        content: 'Breadth recovered\nVolume remained muted.',
        recordedAt: '2026-07-15T10:00:00Z',
        updatedAt: '2026-07-15T10:00:00Z',
        signal: null, interpretation: null, mentalState: null, tags: ['breadth'],
        primarySubject: null, relatedSubjects: [], evidence: null,
      },
    }],
    nextCursor: null,
  })))
  renderApp('/today')
  expect(await screen.findByRole('heading', { name: 'Recent observations' })).toBeInTheDocument()
  expect(screen.getByText('Breadth recovered')).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Alerts' })).toBeDisabled()
})

test('calendar deep links remain first-class', async () => {
  server.use(...authenticatedHandlers())
  renderApp('/calendar/2026/07')
  expect(await screen.findByRole('heading', { name: 'Calendar' })).toBeInTheDocument()
  expect(window.location.pathname).toBe('/calendar/2026/07')
})

test('empty watchlist explains its purpose and the next step', async () => {
  server.use(...authenticatedHandlers())
  server.use(http.get('/api/app/watchlist', () => HttpResponse.json({ items: [] })))
  renderApp('/watchlist')
  expect(await screen.findByRole('heading', { name: 'Add an instrument to follow' })).toBeInTheDocument()
  expect(screen.getByText('After adding it, note why it matters and revisit its observation timeline when new evidence appears.')).toBeInTheDocument()
  expect(await screen.findByText('Nothing to follow yet')).toBeInTheDocument()
  expect(screen.getByText('Choose an instrument above to start a focused observation list.')).toBeInTheDocument()
  expect(screen.getByRole('combobox', { name: /Instrument/ })).toBeDisabled()
  expect(screen.getByText('No instruments are available to add right now.')).toBeInTheDocument()
})

test('comparison explains how to create an Agent when none are available', async () => {
  server.use(...authenticatedHandlers())
  server.use(http.get('/api/app/agents', () => HttpResponse.json({ items: [] })))
  renderApp('/review')
  const agent = await screen.findByRole('combobox', { name: 'Agent' })
  expect(agent).toBeDisabled()
  expect(await screen.findByText(/No Agent Users are available yet/)).toBeInTheDocument()
})

test('comparison keeps Human and Agent records explicitly separate and read-only', async () => {
  server.use(...authenticatedHandlers())
  server.use(http.get('/api/app/comparison', () => HttpResponse.json({
    human: {
      ownerId: bootstrap.currentUser.id,
      ownerType: 'human',
      availability: 'available',
      observations: [{
        journalDay: '2026-07-16',
        update: {
          id: '33333333-3333-3333-3333-333333333333',
          content: 'Human breadth view',
          recordedAt: '2026-07-16T10:00:00Z',
          updatedAt: '2026-07-16T10:00:00Z',
          signal: null, interpretation: null, mentalState: null, tags: [],
          primarySubject: { type: 'theme', name: 'AI', instrumentId: null, market: null, symbol: null, displayName: null, dailyCloseAvailable: false, dailyCloseStatus: 'unsupported', dailyClose: null },
          relatedSubjects: [], evidence: null,
        },
        expectations: [{ id: '44444444-4444-4444-4444-444444444444', expectedBehavior: 'Human expectation', deadline: '2026-07-17T10:00:00Z', invalidationCondition: 'Below support', confidence: 'medium', market: 'US', outcome: 'confirmed', reasoningQuality: 'sound', reviewExplanation: null }],
      }],
    },
    agent: {
      ownerId: '22222222-2222-2222-2222-222222222222',
      ownerType: 'agent',
      availability: 'available',
      observations: [{
        journalDay: '2026-07-16',
        update: {
          id: '55555555-5555-5555-5555-555555555555',
          content: 'Agent breadth view',
          recordedAt: '2026-07-16T10:00:00Z',
          updatedAt: '2026-07-16T10:00:00Z',
          signal: null, interpretation: null, mentalState: null, tags: [],
          primarySubject: { type: 'theme', name: 'AI', instrumentId: null, market: null, symbol: null, displayName: null, dailyCloseAvailable: false, dailyCloseStatus: 'unsupported', dailyClose: null },
          relatedSubjects: [], evidence: null,
        },
        expectations: [{ id: '66666666-6666-6666-6666-666666666666', expectedBehavior: 'Agent expectation', deadline: '2026-07-17T10:00:00Z', invalidationCondition: 'Above resistance', confidence: 'high', market: 'US', outcome: 'invalidated', reasoningQuality: 'mixed', reviewExplanation: null }],
      }],
    },
    difference: { outcomeConsistent: false, confidenceDifference: 1 },
  })))
  renderApp('/review')
  expect(await screen.findByText('Choose who and what to compare')).toBeInTheDocument()
  expect(screen.getByText('Select an Agent, a subject, and a date range.')).toBeInTheDocument()
  expect(screen.getByText('Your comparison will appear here.')).toBeInTheDocument()
  await screen.findByRole('option', { name: 'Atlas' })
  await userEvent.selectOptions(screen.getByLabelText('Agent'), '22222222-2222-2222-2222-222222222222')
  await userEvent.type(screen.getByLabelText('Subject'), 'AI')
  await userEvent.click(screen.getByRole('button', { name: 'Compare' }))
  expect(await screen.findByText('Human breadth view')).toBeInTheDocument()
  expect(screen.getByText('Agent breadth view')).toBeInTheDocument()
  expect(screen.getByText('Human-owned records')).toBeInTheDocument()
  expect(screen.getByText('Agent-owned records')).toBeInTheDocument()
  expect(screen.getByText('Different')).toBeInTheDocument()
  expect(screen.getByRole('heading', { name: 'Comparison result' })).toBeInTheDocument()
  expect(screen.getByText('These differences describe the records; they do not decide which view is better.')).toBeInTheDocument()
})

test.each([
  '/diary',
  '/monthly-review',
  '/alerts',
  '/price-alerts',
  '/rotation',
  '/partners',
  '/articles',
  '/research',
])('removed product route %s has no compatibility screen', async route => {
  server.use(...authenticatedHandlers())
  renderApp(route)
  expect(await screen.findByText('Page not found.')).toBeInTheDocument()
})
