import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { expect, test } from 'vitest'
import { AuthProvider } from '../auth/AuthProvider'
import type { Expectation } from '../features/api'
import { I18nProvider } from '../i18n'
import { ExpectationSelfReviewForm } from '../pages/review/ExpectationSelfReviewForm'
import { server } from './setup'

const expectation: Expectation = {
  id: '11111111-1111-1111-1111-111111111111',
  observationUpdateId: '22222222-2222-2222-2222-222222222222',
  marketObservationId: '33333333-3333-3333-3333-333333333333',
  journalDay: '2026-07-14',
  expectedBehavior: 'Breadth should remain above 60%',
  deadline: '2026-07-17T20:00:00Z',
  invalidationCondition: 'Breadth closes below 45%',
  confidence: 'medium',
  market: 'US',
  invalidatedAt: '2026-07-16T20:00:00Z',
  readiness: 'ready_for_review',
  deadlineElapsed: false,
  createdAt: '2026-07-14T12:00:00Z',
  updatedAt: '2026-07-16T20:00:00Z',
}

function renderReview() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  render(<QueryClientProvider client={client}><AuthProvider><I18nProvider><ExpectationSelfReviewForm expectation={expectation} onClose={() => undefined} /></I18nProvider></AuthProvider></QueryClientProvider>)
}

test('Guided Review renders exact source, decisions, execution, and trades without scanning history', async () => {
  let historyCalls = 0
  server.use(
    http.post('/api/auth/refresh', () => new HttpResponse(null, { status: 401 })),
    http.get(`/api/app/expectations/${expectation.id}/review`, () => new HttpResponse(null, { status: 404 })),
    http.get('/api/app/reasoning-labels', () => HttpResponse.json({ items: [] })),
    http.get('/api/app/market-observations', () => { historyCalls += 1; return HttpResponse.json({ items: [], nextCursor: null }) }),
    http.get(`/api/app/expectations/${expectation.id}/review-context`, () => HttpResponse.json({
      expectationId: expectation.id,
      observationUpdateId: expectation.observationUpdateId,
      availability: 'available',
      unavailableContext: [],
      marketObservation: { id: expectation.marketObservationId, journalDay: expectation.journalDay },
      observationUpdate: {
        id: expectation.observationUpdateId,
        content: 'Exact source beyond the first history page',
        recordedAt: '2026-07-14T12:00:00Z',
        updatedAt: '2026-07-14T12:00:00Z',
        signal: 'Advancers exceeded decliners',
        interpretation: 'Participation may be broadening',
        mentalState: null,
        tags: [],
        primarySubject: null,
        relatedSubjects: [],
        evidence: { url: 'https://example.com/breadth', title: 'Closing breadth', quote: 'Advancers led.' },
      },
      actionDecisions: [{
        decision: {
          id: '44444444-4444-4444-4444-444444444444',
          observationUpdateId: expectation.observationUpdateId,
          expectationId: expectation.id,
          intent: 'avoid_trade',
          reason: 'Wait for broader confirmation.',
          recordedAt: '2026-07-14T13:00:00Z',
          executionReview: 'followed',
          updatedAt: '2026-07-14T13:00:00Z',
        },
        trades: [{
          id: '55555555-5555-5555-5555-555555555555',
          actionDecisionId: '44444444-4444-4444-4444-444444444444',
          symbol: 'SPY', side: 'sell', quantity: 1, price: 620, currency: 'USD',
          executedAt: '2026-07-14T15:00:00Z', note: 'Reduced exposure.',
          createdAt: '2026-07-14T15:00:00Z', updatedAt: '2026-07-14T15:00:00Z',
        }],
      }],
    })),
  )

  renderReview()

  expect(await screen.findByText('Exact source beyond the first history page')).toBeInTheDocument()
  expect(screen.getByText(/Advancers exceeded decliners/)).toBeInTheDocument()
  expect(screen.getByText(/Participation may be broadening/)).toBeInTheDocument()
  expect(screen.getByRole('link', { name: 'Closing breadth' })).toHaveAttribute('href', 'https://example.com/breadth')
  expect(screen.getByText('Wait for broader confirmation.')).toBeInTheDocument()
  expect(screen.getByText('Execution review: Followed')).toBeInTheDocument()
  expect(screen.getByText('sell 1 SPY at 620 USD')).toBeInTheDocument()
  expect(historyCalls).toBe(0)
})

test('Guided Review explicitly marks partial context without substituting another update', async () => {
  server.use(
    http.post('/api/auth/refresh', () => new HttpResponse(null, { status: 401 })),
    http.get(`/api/app/expectations/${expectation.id}/review`, () => new HttpResponse(null, { status: 404 })),
    http.get('/api/app/reasoning-labels', () => HttpResponse.json({ items: [] })),
    http.get(`/api/app/expectations/${expectation.id}/review-context`, () => HttpResponse.json({
      expectationId: expectation.id,
      observationUpdateId: expectation.observationUpdateId,
      availability: 'partial',
      unavailableContext: ['observation_update', 'market_observation', 'action_decisions', 'trades'],
      marketObservation: null,
      observationUpdate: null,
      actionDecisions: [],
    })),
  )

  renderReview()

  expect(await screen.findByText('Some source context is no longer available. Only the retained records shown here are included.')).toBeInTheDocument()
  expect(screen.queryByText('Exact source beyond the first history page')).not.toBeInTheDocument()
})
