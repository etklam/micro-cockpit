import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { expect, test } from 'vitest'
import { I18nProvider } from '../i18n'
import { AuthProvider } from '../auth/AuthProvider'
import { PatternReviewSection } from '../pages/review/PatternReviewSection'
import { ActionDecisionPanel } from '../pages/today/ActionDecisionPanel'
import { server } from './setup'

test('review evidence can become a confirmed pattern, a sourced principle, and future decision context', async () => {
  const patternId = '11111111-1111-1111-1111-111111111111'
  let confirmed = false
  let hasPattern = false
  let principleBody: Record<string, unknown> | null = null
  let principles: unknown[] = []
  server.use(
    http.post('/api/auth/refresh', () => new HttpResponse(null, { status: 401 })),
    http.get('/api/app/pattern-review', () => HttpResponse.json({
      from: '2026-07-01', to: '2026-07-31', reviewedExpectationCount: 3,
      labels: [{
        kind: 'issue', key: 'insufficient_evidence', name: 'Insufficient evidence', system: true,
        count: 2, denominator: 3, confirmedPatternId: hasPattern ? patternId : null,
        patternIsConfirmed: confirmed,
        firstSeen: '2026-07-08T10:00:00Z', mostRecent: '2026-07-15T10:00:00Z',
        evidence: [{
          expectationId: '22222222-2222-2222-2222-222222222222',
          reviewId: '33333333-3333-3333-3333-333333333333', journalDay: '2026-07-15', subject: 'NVDA',
          expectedBehavior: 'Leadership broadens after earnings.', outcome: 'invalidated', reasoningQuality: 'mixed',
          observationExcerpt: 'Leadership remained narrow into the close.', reviewExplanation: 'The breadth signal did not confirm.',
          reviewedAt: '2026-07-15T10:00:00Z', url: '/review?expectationId=22222222-2222-2222-2222-222222222222',
        }],
        trend: {
          status: 'insufficient_evidence', direction: null,
          current: { from: '2026-07-01', to: '2026-07-31', occurrenceCount: 2, reviewedExpectationCount: 3, evidence: [] },
          previous: null,
        },
      }],
    })),
    http.post('/api/app/confirmed-patterns', () => {
      hasPattern = true
      confirmed = true
      return HttpResponse.json({ id: patternId, kind: 'issue', key: 'insufficient_evidence', name: 'Insufficient evidence', system: true, isConfirmed: true, firstConfirmedAt: '2026-07-16T10:00:00Z', confirmedAt: '2026-07-16T10:00:00Z', unconfirmedAt: null, updatedAt: '2026-07-16T10:00:00Z' })
    }),
    http.delete(`/api/app/confirmed-patterns/${patternId}`, () => { confirmed = false; return new HttpResponse(null, { status: 204 }) }),
    http.post('/api/app/discipline-principles', async ({ request }) => {
      principleBody = await request.json() as Record<string, unknown>
      const item = { id: '44444444-4444-4444-4444-444444444444', content: principleBody.content, status: 'active', selectedForToday: false, confirmedPatternId: patternId, confirmedPatternLabel: 'Insufficient evidence', createdAt: '2026-07-16T10:00:00Z', updatedAt: '2026-07-16T10:00:00Z' }
      principles = [item]
      return HttpResponse.json(item, { status: 201 })
    }),
    http.get('/api/app/discipline-principles', () => HttpResponse.json({ items: principles })),
    http.get('/api/app/observation-updates/update-1/action-decisions', () => HttpResponse.json({ items: [] })),
  )
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  render(<QueryClientProvider client={client}><AuthProvider><I18nProvider><PatternReviewSection /><ActionDecisionPanel updateId="update-1" expectations={[]} /></I18nProvider></AuthProvider></QueryClientProvider>)

  expect(await screen.findByText('“Insufficient evidence” appeared in 2 of 3 reviewed Expectations.')).toBeInTheDocument()
  expect(screen.getByText('No reviewed Expectations in one of the comparison periods.')).toBeInTheDocument()
  await userEvent.click(screen.getByText('Related review evidence (1)'))
  expect(screen.getByText('Leadership remained narrow into the close.')).toBeInTheDocument()
  expect(screen.getByRole('link', { name: 'Open source review' })).toHaveAttribute('href', '/review?expectationId=22222222-2222-2222-2222-222222222222')

  await userEvent.click(screen.getByRole('button', { name: 'Confirm as meaningful pattern' }))
  await userEvent.click(await screen.findByRole('button', { name: 'Mark as no longer confirmed' }))
  expect(await screen.findByText('No longer confirmed')).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Create Discipline Principle' })).not.toBeInTheDocument()
  await userEvent.click(screen.getByRole('button', { name: 'Confirm again' }))
  await userEvent.click(await screen.findByRole('button', { name: 'Create Discipline Principle' }))
  await userEvent.type(screen.getByRole('textbox', { name: /^Discipline Principle/ }), 'Look for counter-evidence before acting.')
  await userEvent.click(screen.getByRole('button', { name: 'Add' }))
  await waitFor(() => expect(principleBody).toEqual({ content: 'Look for counter-evidence before acting.', confirmedPatternId: patternId }))

  await userEvent.click(screen.getByRole('button', { name: 'Add decision' }))
  expect(await screen.findByText('Your active discipline principles (1)')).toBeInTheDocument()
  await userEvent.click(screen.getByText('Your active discipline principles (1)'))
  expect(screen.getByText('Look for counter-evidence before acting.')).toBeInTheDocument()
})
