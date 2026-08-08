import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { delay, http, HttpResponse } from 'msw'
import { expect, test } from 'vitest'
import { AuthProvider } from '../auth/AuthProvider'
import { I18nProvider } from '../i18n'
import { PatternReviewSection } from '../pages/review/PatternReviewSection'
import { server } from './setup'

const evidence = (reviewId: string, journalDay: string) => ({
  expectationId: `${reviewId.slice(0, -1)}9`, reviewId, journalDay, subject: 'NVDA',
  expectedBehavior: 'Participation broadens.', outcome: 'confirmed', reasoningQuality: 'sound',
  observationExcerpt: `Evidence ${reviewId}`, reviewExplanation: null,
  reviewedAt: `${journalDay}T12:00:00Z`, url: `/review?expectationId=${reviewId.slice(0, -1)}9`,
})

function renderSection() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  return render(<QueryClientProvider client={client}><AuthProvider><I18nProvider><PatternReviewSection /></I18nProvider></AuthProvider></QueryClientProvider>)
}

test('shows a supported trend with raw shares and evidence for both windows', async () => {
  server.use(
    http.post('/api/auth/refresh', () => new HttpResponse(null, { status: 401 })),
    http.get('/api/app/pattern-review', () => HttpResponse.json({
      from: '2026-07-08', to: '2026-07-14', reviewedExpectationCount: 5,
      labels: [{
        kind: 'issue', key: 'insufficient_evidence', name: 'Insufficient evidence', system: true,
        count: 3, denominator: 5, confirmedPatternId: null,
        firstSeen: '2026-07-08T12:00:00Z', mostRecent: '2026-07-10T12:00:00Z',
        evidence: [evidence('11111111-1111-1111-1111-111111111111', '2026-07-08')],
        trend: {
          status: 'supported', direction: 'higher',
          current: { from: '2026-07-08', to: '2026-07-14', occurrenceCount: 3, reviewedExpectationCount: 5, evidence: [evidence('11111111-1111-1111-1111-111111111111', '2026-07-08')] },
          previous: { from: '2026-07-01', to: '2026-07-07', occurrenceCount: 1, reviewedExpectationCount: 5, evidence: [evidence('22222222-2222-2222-2222-222222222222', '2026-07-01')] },
        },
      }],
    })),
  )
  renderSection()

  expect(await screen.findByText('The observed share was higher in this period: 3 of 5 (60.0%), compared with 1 of 5 (20.0%).')).toBeInTheDocument()
  await userEvent.click(screen.getByText(/Current period through/))
  await userEvent.click(screen.getByText(/Previous comparison period/))
  expect(screen.getByText('Related review evidence (1)')).toBeInTheDocument()
  expect(screen.getAllByRole('link', { name: 'Open source review' })).toHaveLength(3)
  expect(screen.getAllByText('Evidence 11111111-1111-1111-1111-111111111111')).toHaveLength(2)
  expect(screen.getByText('Evidence 22222222-2222-2222-2222-222222222222')).toBeInTheDocument()
})

test('shows insufficient evidence without a directional claim', async () => {
  server.use(
    http.post('/api/auth/refresh', () => new HttpResponse(null, { status: 401 })),
    http.get('/api/app/pattern-review', () => HttpResponse.json({
      from: '2026-07-08', to: '2026-07-14', reviewedExpectationCount: 4,
      labels: [{ kind: 'strength', key: 'clear_invalidation', name: 'Clear invalidation', system: true, count: 1, denominator: 4, confirmedPatternId: null, firstSeen: null, mostRecent: null, evidence: [],
        trend: { status: 'insufficient_evidence', direction: null,
          current: { from: '2026-07-08', to: '2026-07-14', occurrenceCount: 1, reviewedExpectationCount: 4, evidence: [] },
          previous: { from: '2026-07-01', to: '2026-07-07', occurrenceCount: 2, reviewedExpectationCount: 5, evidence: [] } } }],
    })),
  )
  renderSection()

  expect(await screen.findByText('Not enough reviewed Expectations in both periods to compare.')).toBeInTheDocument()
  expect(screen.queryByText(/observed share was (higher|lower|the same)/i)).not.toBeInTheDocument()
})

test('keeps explicit loading and error states', async () => {
  server.use(
    http.post('/api/auth/refresh', () => new HttpResponse(null, { status: 401 })),
    http.get('/api/app/pattern-review', async () => { await delay(50); return new HttpResponse(null, { status: 500 }) }),
  )
  renderSection()

  expect(screen.getByRole('status', { name: 'Loading' })).toBeInTheDocument()
  expect(await screen.findByRole('alert')).toHaveTextContent('Couldn’t reach the cockpit.')
  expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument()
})
