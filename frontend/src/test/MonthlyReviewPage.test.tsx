import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { BrowserRouter } from 'react-router-dom'
import { expect, test } from 'vitest'
import App from '../App'
import { AuthProvider } from '../auth/AuthProvider'
import { I18nProvider } from '../i18n'
import type { CalendarResponse, DiaryReviewSummaryResponse } from '../generated/edge'
import reviewPageSource from '../MonthlyReviewPage.tsx?raw'
import { server } from './setup'

const bootstrap = {
  currentUser: { id: 'user-1', email: 'owner@example.com', displayName: 'Owner' },
  timezone: 'Asia/Taipei', baseCurrency: 'HKD', appearance: 'system', accentTheme: 'green', locale: 'en', role: 'user', accountType: 'human', currentLocalDate: '2028-02-14',
  availableProductAreas: ['today', 'diary', 'calendar'],
}

const summary = {
  reviewedCount: 1, averageDisciplineScore: 4, averageExecutionScore: null,
  emotionCounts: { calm: 1 }, processAssessmentCounts: { good: 1 },
  topMistakeTags: [{ tag: 'poor_timing', count: 1 }],
}

function handlers(calendar: CalendarResponse = {
  year: 2028, month: 2,
  summary: { year: 2028, month: 2, total: 0, recordedDays: 2, profitDays: 1, lossDays: 0, flatDays: 1, bestDay: 25, worstDay: 0 },
  days: [
    { date: '2028-02-01', performance: null, diaryCount: 0, transactionCount: 0, alertCount: null },
    { date: '2028-02-02', performance: { localDate: '2028-02-02', pnlAmount: 0, capitalBase: null, pnlPercent: null, note: '' }, diaryCount: 1, transactionCount: 0, alertCount: null },
    { date: '2028-02-03', performance: { localDate: '2028-02-03', pnlAmount: 25, capitalBase: null, pnlPercent: null, note: '' }, diaryCount: 2, transactionCount: 0, alertCount: null },
  ], capabilities: { alerts: 'unavailable' },
}, review: DiaryReviewSummaryResponse = summary) {
  return [
    http.post('/api/auth/refresh', () => HttpResponse.json({ accessToken: 'memory-token', expiresAt: '2028-02-15T00:00:00Z' })),
    http.get('/api/app/bootstrap', () => HttpResponse.json(bootstrap)),
    http.get('/api/app/calendar', () => HttpResponse.json(calendar)),
    http.get('/api/app/diary-review-summary', () => HttpResponse.json(review)),
    http.get('/api/app/diary-review-items', () => HttpResponse.json({ items: [], nextCursor: null })),
  ]
}

function renderReview(path = '/review') {
  window.history.replaceState({}, '', path)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(<QueryClientProvider client={client}><BrowserRouter><AuthProvider><I18nProvider><App /></I18nProvider></AuthProvider></BrowserRouter></QueryClientProvider>)
}

test('/review uses bootstrap local date and replaces with its canonical month URL', async () => {
  server.use(...handlers())
  renderReview()
  expect(await screen.findByRole('heading', { name: 'Monthly review' })).toBeInTheDocument()
  expect(window.location.pathname).toBe('/review/2028/02')
})

test('noncanonical valid review month is replaced with the canonical month URL', async () => {
  server.use(...handlers())
  renderReview('/review/2028/2')
  await screen.findByRole('heading', { name: 'Monthly review' })
  expect(window.location.pathname).toBe('/review/2028/02')
})

test.each(['/review/2028/13', '/review/foo/02'])('invalid review path redirects through bootstrap month', async path => {
  server.use(...handlers())
  renderReview(path)
  await screen.findByRole('heading', { name: 'Monthly review' })
  expect(window.location.pathname).toBe('/review/2028/02')
})

test('month navigation updates the URL', async () => {
  server.use(...handlers())
  renderReview('/review/2028/02')
  await userEvent.click(await screen.findByRole('button', { name: 'Previous month' }))
  expect(window.location.pathname).toBe('/review/2028/01')
  await userEvent.click(screen.getByRole('button', { name: 'Next month' }))
  expect(window.location.pathname).toBe('/review/2028/02')
})

test('leap-year calendar and review queries use the selected full month', async () => {
  let calendarQuery = ''
  let reviewQuery = ''
  server.use(...handlers())
  server.use(http.get('/api/app/calendar', ({ request }) => { calendarQuery = new URL(request.url).search; return HttpResponse.json({ year: 2028, month: 2, summary: null, days: [], capabilities: { alerts: 'empty' } }) }),
    http.get('/api/app/diary-review-summary', ({ request }) => { reviewQuery = new URL(request.url).search; return HttpResponse.json({ ...summary, reviewedCount: 0 }) }))
  renderReview('/review/2028/02')
  await screen.findByRole('heading', { name: 'Monthly review' })
  await waitFor(() => expect(calendarQuery).toContain('year=2028'))
  expect(calendarQuery).toContain('month=2')
  expect(reviewQuery).toContain('from=2028-02-01')
  expect(reviewQuery).toContain('to=2028-02-29')
})

test('missing P/L is omitted while an actual zero is shown as a flat day', async () => {
  server.use(...handlers())
  renderReview('/review/2028/02')
  const chart = await screen.findByRole('list', { name: 'Daily P/L for February 2028' })
  expect(within(chart).queryByLabelText(/February 1/)).not.toBeInTheDocument()
  expect(within(chart).getByLabelText('February 2, P/L 0, flat')).toBeInTheDocument()
  expect(within(chart).getByRole('link', { name: /February 2/ })).toHaveAttribute('href', '/calendar/2028/02?day=2028-02-02')
  expect(screen.getByText('Flat days').nextSibling).toHaveTextContent('1')
})

test('review coverage uses the sum of the selected month diary counts', async () => {
  server.use(...handlers())
  renderReview('/review/2028/02')
  expect(await screen.findByText('1 of 3 diaries (33.3%)')).toBeInTheDocument()
  expect(screen.getByText('Average execution').nextSibling).toHaveTextContent('Unavailable')
  expect(screen.getByText('Completion').nextSibling).toHaveTextContent('2 diaries still need review')
})

test('evidence filters use selected-month backend queries and clear cursor in URL', async () => {
  let requested = ''
  server.use(...handlers())
  server.use(http.get('/api/app/diary-review-items', ({ request }) => {
    requested = new URL(request.url).search
    return HttpResponse.json({ items: [
      { diaryId: 'diary-1', localDate: '2028-02-03', title: 'Plan review', contentPreview: 'Evidence', reviewStatus: 'unreviewed', processAssessment: null, emotion: null, disciplineScore: null, executionScore: null, mistakeTags: [], lesson: null, nextAction: null, reviewUpdatedAt: null },
      { diaryId: 'diary-2', localDate: '2028-02-02', title: 'Completed review', contentPreview: 'Evidence', reviewStatus: 'reviewed', processAssessment: 'good', emotion: 'calm', disciplineScore: 4, executionScore: 3, mistakeTags: ['no_plan'], lesson: 'Lesson', nextAction: 'Next', reviewUpdatedAt: '2028-02-02T00:00:00Z' },
    ], nextCursor: null })
  }))
  renderReview('/review/2028/02?reviewStatus=reviewed&cursor=old')
  expect(await screen.findByRole('link', { name: 'Complete review' })).toHaveAttribute('href', '/diary/diary-1#decision-review')
  expect(screen.getByRole('link', { name: 'Open review' })).toHaveAttribute('href', '/diary/diary-2#decision-review')
  await waitFor(() => expect(requested).toContain('from=2028-02-01'))
  expect(requested).toContain('to=2028-02-29')
  expect(requested).toContain('status=reviewed')
  await userEvent.selectOptions(screen.getByLabelText('Assessment'), 'poor')
  expect(window.location.search).toContain('reviewStatus=reviewed')
  expect(window.location.search).toContain('assessment=poor')
  expect(window.location.search).not.toContain('cursor=')
})

test('evidence load-more failure preserves the already loaded items', async () => {
  server.use(...handlers())
  server.use(http.get('/api/app/diary-review-items', ({ request }) => {
    if (new URL(request.url).searchParams.get('cursor')) return new HttpResponse(null, { status: 503 })
    return HttpResponse.json({ items: [{ diaryId: 'diary-1', localDate: '2028-02-03', title: 'First evidence', contentPreview: 'Notes', reviewStatus: 'reviewed', processAssessment: 'good', emotion: null, disciplineScore: 4, executionScore: null, mistakeTags: [], lesson: null, nextAction: null, reviewUpdatedAt: '2028-02-03T00:00:00Z' }], nextCursor: 'next-page' })
  }))
  renderReview('/review/2028/02')
  expect(await screen.findByText('First evidence')).toBeInTheDocument()
  await userEvent.click(screen.getByRole('button', { name: 'Load more' }))
  expect(await screen.findByText('Could not load more review evidence.')).toBeInTheDocument()
  expect(screen.getByText('First evidence')).toBeInTheDocument()
})

test('browser back and forward restore evidence filters', async () => {
  server.use(...handlers())
  renderReview('/review/2028/02')
  const filter = await screen.findByLabelText('Assessment')
  await userEvent.selectOptions(filter, 'good')
  await waitFor(() => expect(window.location.search).toContain('assessment=good'))
  await userEvent.selectOptions(filter, 'poor')
  await waitFor(() => expect(window.location.search).toContain('assessment=poor'))
  window.history.back()
  await waitFor(() => expect(screen.getByLabelText('Assessment')).toHaveValue('good'))
  window.history.forward()
  await waitFor(() => expect(screen.getByLabelText('Assessment')).toHaveValue('poor'))
})

test('zero diaries makes coverage unavailable without hiding a zero review count', async () => {
  const calendar: CalendarResponse = { year: 2028, month: 2, summary: null, days: [], capabilities: { alerts: 'empty' } }
  server.use(...handlers(calendar, { ...summary, reviewedCount: 0 }))
  renderReview('/review/2028/02')
  expect((await screen.findByText('Reviewed entries')).nextSibling).toHaveTextContent('0')
  expect(screen.getByText('Review coverage').nextSibling).toHaveTextContent('Unavailable')
})

test('outcome and process remain separate when review service is unavailable', async () => {
  server.use(...handlers())
  server.use(http.get('/api/app/diary-review-summary', () => new HttpResponse(null, { status: 503 })))
  renderReview('/review/2028/02')
  const outcome = await screen.findByRole('region', { name: 'Outcome' })
  const process = screen.getByRole('region', { name: 'Process' })
  expect(await within(outcome).findByText('Net P/L')).toBeInTheDocument()
  expect(within(process).getByText('Process data is unavailable.')).toBeInTheDocument()
  expect(within(process).getByRole('button', { name: 'Try again' })).toBeInTheDocument()
  expect(screen.getByText('Process and outcome are reviewed separately.')).toBeInTheDocument()
})

test('evidence failure does not hide outcome or process summary', async () => {
  server.use(...handlers())
  server.use(http.get('/api/app/diary-review-items', () => new HttpResponse(null, { status: 504 })))
  renderReview('/review/2028/02')
  expect(await screen.findByText('Review evidence is unavailable.')).toBeInTheDocument()
  expect(screen.getByText('Net P/L')).toBeInTheDocument()
  expect(screen.getByText('Reviewed entries')).toBeInTheDocument()
})

test('an empty outcome differs from unavailable performance', async () => {
  const empty: CalendarResponse = {
    year: 2028,
    month: 2,
    summary: { year: 2028, month: 2, total: 0, recordedDays: 0, profitDays: 0, lossDays: 0, flatDays: 0, bestDay: null, worstDay: null },
    days: [],
    capabilities: { alerts: 'empty' },
  }
  server.use(...handlers(empty))
  renderReview('/review/2028/02')
  expect(await screen.findByText('No performance recorded this month')).toBeInTheDocument()
  expect(screen.queryByText('Net P/L')).not.toBeInTheDocument()
  cleanup()
  server.resetHandlers()
  server.use(...handlers())
  server.use(http.get('/api/app/calendar', () => new HttpResponse(null, { status: 504 })))
  renderReview('/review/2028/02')
  expect(await screen.findByText('Outcome data is unavailable.')).toBeInTheDocument()
})

test('calendar offers a monthly review link that preserves its month', async () => {
  server.use(...handlers())
  renderReview('/calendar/2028/02')
  const link = await screen.findByRole('link', { name: 'Review this month' })
  expect(link).toHaveAttribute('href', '/review/2028/02')
})

test('monthly review stays behind typed query hooks without raw transport access', () => {
  expect(reviewPageSource).not.toMatch(/\bfetch\s*\(/)
  expect(reviewPageSource).not.toContain('/api/app/')
  expect(reviewPageSource).not.toMatch(/https?:\/\//)
})
