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

const userId = '11111111-1111-1111-1111-111111111111'
const updateId = '22222222-2222-2222-2222-222222222222'

const bootstrap = {
  currentUser: { id: userId, email: 'owner@example.com', displayName: 'Owner' },
  timezone: 'Asia/Taipei', journalDayRollover: '00:00', baseCurrency: 'USD',
  appearance: 'system', accentTheme: 'green', locale: 'en', role: 'user', accountType: 'human',
  currentJournalDay: '2026-07-16', availableProductAreas: ['today', 'review', 'watchlist', 'calendar', 'tools', 'settings'],
}

const observation = {
  id: '33333333-3333-3333-3333-333333333333', journalDay: '2026-07-16', timezone: 'Asia/Taipei', rollover: '00:00',
  updates: [{
    id: updateId, content: 'Breadth recovered\nVolume stayed muted.', recordedAt: '2026-07-16T10:00:00Z', updatedAt: '2026-07-16T10:00:00Z',
    signal: null, interpretation: null, mentalState: null, tags: ['breadth'], primarySubject: null, relatedSubjects: [], evidence: null,
  }],
}

function handlers({ today = null, history = [], expectations = [], discipline = null }: { today?: unknown; history?: unknown[]; expectations?: unknown[]; discipline?: unknown } = {}) {
  return [
    http.post('/api/auth/refresh', () => HttpResponse.json({ accessToken: 'memory-only-token', expiresAt: '2026-07-16T12:00:00Z' })),
    http.get('/api/app/bootstrap', () => HttpResponse.json(bootstrap)),
    http.get('/api/app/market-observations/today', () => today ? HttpResponse.json(today) : new HttpResponse(null, { status: 404 })),
    http.get('/api/app/market-observations', () => HttpResponse.json({ items: history, nextCursor: null })),
    http.get('/api/app/expectations', () => HttpResponse.json({ items: expectations })),
    http.get('/api/app/discipline-principles/today', () => discipline ? HttpResponse.json(discipline) : new HttpResponse(null, { status: 404 })),
    http.get('/api/app/market/symbols', () => HttpResponse.json({ items: [] })),
    http.get('/api/app/observation-updates/:id/action-decisions', () => HttpResponse.json({ items: [] })),
  ]
}

function renderApp() {
  window.history.replaceState({}, '', '/today')
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(<QueryClientProvider client={client}><BrowserRouter><AuthProvider><I18nProvider><App /></I18nProvider></AuthProvider></BrowserRouter></QueryClientProvider>)
}

test('empty Today offers a compact next step and keeps the composer focusable', async () => {
  server.use(...handlers())
  renderApp()

  expect(await screen.findByText('No observations yet')).toBeInTheDocument()
  expect(screen.getAllByRole('link', { name: 'All observations' }).at(-1)).toHaveAttribute('href', '/today/observations')
  const captureButtons = screen.getAllByRole('button', { name: 'New observation' })
  await userEvent.click(captureButtons[captureButtons.length - 1])
  expect(screen.getByLabelText('What did you notice today?')).toHaveFocus()
})

test('quick capture preserves a draft while saving and disables the pending action', async () => {
  let release!: () => void
  let requestContent = ''
  const pending = new Promise<void>(resolve => { release = resolve })
  server.use(
    ...handlers(),
    http.post('/api/app/quick-observations', async ({ request }) => {
      requestContent = ((await request.json()) as { content: string }).content
      await pending
      return HttpResponse.json({})
    }),
  )
  renderApp()

  const composer = await screen.findByLabelText('What did you notice today?')
  await userEvent.type(composer, 'Breadth recovered into the close.')
  expect(screen.getByText('Draft is kept on this page until you save it.')).toBeInTheDocument()
  const save = screen.getByRole('button', { name: 'Save observation' })
  await userEvent.click(save)
  await waitFor(() => expect(save).toBeDisabled())
  expect(composer).toHaveValue('Breadth recovered into the close.')
  release()
  await waitFor(() => expect(requestContent).toBe('Breadth recovered into the close.'))
  await waitFor(() => expect(composer).toHaveValue(''))
})

test('populated Today shows real summary counts, discipline, and recent Journal Day links', async () => {
  const history = [1, 2, 3].map((index, offset) => ({
    marketObservationId: `44444444-4444-4444-4444-${String(index).repeat(12)}`,
    journalDay: `2026-07-${String(15 - offset).padStart(2, '0')}`,
    authorId: userId,
    update: { ...observation.updates[0], id: `55555555-5555-5555-5555-${String(index).repeat(12)}`, content: `Recent observation ${index}` },
  }))
  server.use(...handlers({
    today: observation,
    history,
    expectations: [{
      id: '66666666-6666-6666-6666-666666666666', observationUpdateId: updateId, marketObservationId: observation.id,
      expectedBehavior: 'Breadth holds', deadline: '2026-07-20T12:00:00Z', invalidationCondition: 'Breadth fades', confidence: 'medium', market: 'US',
      invalidatedAt: null, readiness: 'ready_for_review', deadlineElapsed: false, createdAt: '2026-07-16T10:30:00Z', updatedAt: '2026-07-16T10:30:00Z',
    }],
    discipline: { id: '77777777-7777-7777-7777-777777777777', content: 'Keep the process simple.', status: 'active', selectedForToday: true, createdAt: '2026-07-01T00:00:00Z', updatedAt: '2026-07-16T00:00:00Z' },
  }))
  renderApp()

  const summary = await screen.findByRole('heading', { name: 'Today at a glance' })
  const summaryCard = summary.closest('.today-summary-card')
  expect(summaryCard).not.toBeNull()
  await waitFor(() => expect(summaryCard?.textContent).toContain('Observations1'))
  expect(summaryCard?.textContent).toContain('Expectations1')
  expect(screen.getByText('Keep the process simple.')).toBeInTheDocument()
  const recent = screen.getByRole('link', { name: /Recent observation 1/ })
  expect(recent).toHaveAttribute('href', '/today/observations?from=2026-07-15&to=2026-07-15')
})
