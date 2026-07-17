import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { BrowserRouter } from 'react-router-dom'
import { expect, test } from 'vitest'
import App from '../App'
import { AuthProvider } from '../auth/AuthProvider'
import { server } from './setup'

const bootstrap = {
  currentUser: { id: '11111111-1111-1111-1111-111111111111', email: 'owner@example.com', displayName: 'Owner' },
  timezone: 'Asia/Taipei', baseCurrency: 'USD', appearance: 'system', role: 'user', accountType: 'human', currentLocalDate: '2026-07-16',
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
    http.get('/api/app/diary-review-summary', () => HttpResponse.json({ reviewedCount: 0, averageDisciplineScore: null, averageExecutionScore: null, emotionCounts: {}, processAssessmentCounts: {}, topMistakeTags: [] })),
    http.get('/api/app/diary-review-items', () => HttpResponse.json({ items: [], nextCursor: null })),
    http.post('/api/auth/logout', () => new HttpResponse(null, { status: 204 })),
  ]
}

function renderApp(path: string) {
  window.history.replaceState({}, '', path)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(<QueryClientProvider client={client}><BrowserRouter><AuthProvider><App /></AuthProvider></BrowserRouter></QueryClientProvider>)
  return client
}

test('restores a session and renders a deep calendar link', async () => {
  server.use(...authenticatedHandlers())
  renderApp('/calendar/2026/07')
  expect(await screen.findByRole('heading', { name: 'Calendar' })).toBeInTheDocument()
  expect(window.location.pathname).toBe('/calendar/2026/07')
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
