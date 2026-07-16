import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { BrowserRouter } from 'react-router-dom'
import { expect, test } from 'vitest'
import App from '../App'
import { AuthProvider } from '../auth/AuthProvider'
import { server } from './setup'

const bootstrap = {
  currentUser: { id: '11111111-1111-1111-1111-111111111111', email: 'owner@example.com', displayName: 'Owner' },
  timezone: 'Asia/Taipei', baseCurrency: 'USD', role: 'user', accountType: 'human', currentLocalDate: '2026-07-16',
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
    http.get('/api/app/diaries', () => HttpResponse.json({ items: [] })),
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
    http.get('/api/app/diaries/:id', () => HttpResponse.json({ id: 'diary-1', localDate: '2026-07-16', title: 'Direct entry', content: 'Notes', createdAt: '2026-07-16T00:00:00Z', updatedAt: '2026-07-16T00:00:00Z' })),
    http.get('/api/app/diaries/:id/transactions', () => HttpResponse.json({ items: [] })))
  renderApp('/diary/diary-1')
  expect(await screen.findByRole('heading', { name: 'Direct entry' })).toBeInTheDocument()
  expect(await screen.findByText('No trades logged.')).toBeInTheDocument()
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
