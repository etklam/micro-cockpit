import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { BrowserRouter } from 'react-router-dom'
import { expect, test } from 'vitest'
import App from '../App'
import { AuthProvider } from '../auth/AuthProvider'
import type { EvaluationPrice, PriceAlertResponse, PriceAlertStatus, TriggerResponse } from '../generated/edge'
import { server } from './setup'

const bootstrap = {
  currentUser: { id: '11111111-1111-1111-1111-111111111111', email: 'owner@example.com', displayName: 'Owner' },
  timezone: 'Asia/Taipei', baseCurrency: 'USD', role: 'user', accountType: 'human', currentLocalDate: '2026-07-16',
  availableProductAreas: ['price-alerts'],
}

function handlers(options?: { alerts?: unknown; alertsStatus?: number; triggers?: unknown; triggerStatus?: number; createStatus?: number; createBody?: unknown; onCreate?: (body: Record<string, unknown>) => void; onList?: () => void }) {
  return [
    http.post('/api/auth/refresh', () => HttpResponse.json({ accessToken: 'memory-only-token', expiresAt: '2026-07-16T12:00:00Z' })),
    http.get('/api/app/bootstrap', () => HttpResponse.json(bootstrap)),
    http.get('/api/app/price-alerts', () => { options?.onList?.(); return HttpResponse.json(options?.alerts ?? { items: [] }, { status: options?.alertsStatus ?? 200 }) }),
    http.post('/api/app/price-alerts', async ({ request }) => {
      const body = await request.json() as Record<string, unknown>
      options?.onCreate?.(body)
      if (options?.createStatus) return HttpResponse.json(options.createBody ?? {}, { status: options.createStatus })
      return HttpResponse.json({ id: 'alert-new', ...body, status: 'active', baselineClose: null, lastEvaluatedDate: null, createdAt: '2026-07-16T00:00:00Z', updatedAt: '2026-07-16T00:00:00Z' })
    }),
    http.post('/api/app/price-alerts/:id/dismiss', () => new HttpResponse(null, { status: 204 })),
    http.post('/api/app/price-alerts/:id/reactivate', () => new HttpResponse(null, { status: 204 })),
    http.delete('/api/app/price-alerts/:id', () => new HttpResponse(null, { status: 204 })),
    http.get('/api/app/price-alerts/:id/triggers', () => HttpResponse.json(options?.triggers ?? { items: [] }, { status: options?.triggerStatus ?? 200 })),
  ]
}

function renderPriceAlerts() {
  window.history.replaceState({}, '', '/price-alerts')
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  const rendered = render(<QueryClientProvider client={client}><BrowserRouter><AuthProvider><App /></AuthProvider></BrowserRouter></QueryClientProvider>)
  return { client, ...rendered }
}

const alerts = {
  items: [
    { id: 'alert-open', symbol: 'AAPL', conditionType: 'above', threshold: 250, evaluationPrice: 'open', lookbackDays: null, direction: null, status: 'triggered', baselineClose: 225.5, lastEvaluatedDate: '2026-07-15', createdAt: '2026-07-01T00:00:00Z', updatedAt: '2026-07-15T00:00:00Z' },
    { id: 'alert-close', symbol: 'MSFT', conditionType: 'below', threshold: 400, evaluationPrice: 'close', lookbackDays: null, direction: null, status: 'dismissed', baselineClose: null, lastEvaluatedDate: null, createdAt: '2026-07-01T00:00:00Z', updatedAt: '2026-07-01T00:00:00Z' },
  ],
}

test('close is the default and selecting open submits daily-bar semantics', async () => {
  let submitted: Record<string, unknown> | undefined
  server.use(...handlers({ onCreate: body => { submitted = body } }))
  renderPriceAlerts()
  const user = userEvent.setup()

  expect(await screen.findByLabelText('Evaluate using')).toHaveValue('close')
  expect(screen.getByText('Evaluated after the daily bar is published.')).toBeInTheDocument()

  await user.type(screen.getByLabelText('Symbol'), 'aapl')
  await user.type(screen.getByLabelText('Target price'), '250')
  await user.selectOptions(screen.getByLabelText('Evaluate using'), 'open')
  await user.click(screen.getByRole('button', { name: 'Create alert' }))

  await waitFor(() => expect(submitted).toMatchObject({ symbol: 'AAPL', conditionType: 'above', threshold: 250, evaluationPrice: 'open' }))
})

test('alert list identifies its evaluation price and daily evaluation state', async () => {
  server.use(...handlers({ alerts }))
  renderPriceAlerts()

  expect(await screen.findByText('AAPL')).toBeInTheDocument()
  expect(screen.getByText('Evaluate using Open')).toBeInTheDocument()
  expect(screen.getByText('Evaluate using Close')).toBeInTheDocument()
  expect(screen.getByText('Last evaluated 2026-07-15')).toBeInTheDocument()
  expect(screen.getByText('Not evaluated yet')).toBeInTheDocument()
  expect(screen.getByText('Baseline close 225.5')).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Dismiss AAPL alert' })).toBeInTheDocument()
})

test.each([
  [400, { detail: 'symbol_has_no_published_price' }, 'Symbol has no published daily price.'],
  [400, { detail: 'invalid_threshold' }, 'Invalid alert request.'],
  [503, { code: 'downstream_unavailable' }, 'Market data unavailable.'],
  [504, { code: 'downstream_timeout' }, 'Price alert request timed out.'],
] as const)('create status %s has a distinct error state', async (status, body, message) => {
  server.use(...handlers({ createStatus: status, createBody: body }))
  renderPriceAlerts()
  const user = userEvent.setup()
  await user.type(await screen.findByLabelText('Symbol'), 'aapl')
  await user.type(screen.getByLabelText('Target price'), '250')
  await user.click(screen.getByRole('button', { name: 'Create alert' }))
  expect(await screen.findByRole('alert')).toHaveTextContent(message)
})

test('dismiss and reactivate refresh only the alert list and affected trigger history', async () => {
  let listRequests = 0
  server.use(...handlers({ alerts, onList: () => { listRequests += 1 } }))
  const { client } = renderPriceAlerts()
  const user = userEvent.setup()

  await screen.findByText('AAPL')
  client.setQueryData(['price-alerts', 'triggers', 'alert-open'], { items: [{ id: 'cached-trigger' }] })
  await user.click(screen.getByRole('button', { name: 'Dismiss AAPL alert' }))
  await waitFor(() => expect(listRequests).toBe(2))
  expect(client.getQueryData(['price-alerts', 'triggers', 'alert-open'])).toEqual({ items: [{ id: 'cached-trigger' }] })
  expect(client.getQueryState(['price-alerts', 'triggers', 'alert-open'])?.isInvalidated).toBe(true)

  client.setQueryData(['price-alerts', 'triggers', 'alert-close'], { items: [{ id: 'cached-trigger' }] })
  await user.click(screen.getByRole('button', { name: 'Reactivate MSFT alert' }))
  await waitFor(() => expect(listRequests).toBe(3))
  expect(client.getQueryState(['price-alerts', 'triggers', 'alert-close'])?.isInvalidated).toBe(true)
})

test('trigger history displays price type and trigger dismissal timestamps', async () => {
  server.use(...handlers({ alerts, triggers: { items: [
    { id: 'trigger-1', tradingDate: '2026-07-15', observedClose: 249.8, observedPrice: 251.25, priceType: 'open', triggeredAt: '2026-07-15T22:00:00Z', dismissedAt: null },
    { id: 'trigger-2', tradingDate: '2026-07-14', observedClose: 249, observedPrice: 249, priceType: 'close', triggeredAt: '2026-07-14T22:00:00Z', dismissedAt: '2026-07-15T01:00:00Z' },
  ] } }))
  renderPriceAlerts()
  const user = userEvent.setup()

  await screen.findByText('AAPL')
  await user.click(screen.getByRole('button', { name: 'View AAPL trigger history' }))

  expect(await screen.findByText('Open price 251.25')).toBeInTheDocument()
  expect(screen.getByText('Trading date 2026-07-15')).toBeInTheDocument()
  expect(screen.getAllByText(/^Triggered /)).toHaveLength(2)
  expect(screen.getByText(/^Dismissed /)).toBeInTheDocument()
  expect(screen.getByText('Active trigger')).toBeInTheDocument()
})

test('valid empty alerts and trigger history differ from backend unavailability', async () => {
  server.use(...handlers())
  const { unmount } = renderPriceAlerts()
  expect(await screen.findByText('No price alerts')).toBeInTheDocument()
  expect(screen.queryByText('Couldn’t reach the cockpit.')).not.toBeInTheDocument()
  unmount()

  server.use(...handlers({ alertsStatus: 503 }))
  renderPriceAlerts()
  expect(await screen.findByText('Couldn’t reach the cockpit.')).toBeInTheDocument()
  expect(screen.queryByText('No price alerts')).not.toBeInTheDocument()
})

test('an alert without triggers has its own empty history state', async () => {
  server.use(...handlers({ alerts }))
  renderPriceAlerts()
  const user = userEvent.setup()
  await screen.findByText('AAPL')
  await user.click(screen.getByRole('button', { name: 'View AAPL trigger history' }))
  expect(await screen.findByText('No trigger history.')).toBeInTheDocument()
})

test('generated client exposes lowercase price and status unions', () => {
  type Equal<Left, Right> = (<Value>() => Value extends Left ? 1 : 2) extends (<Value>() => Value extends Right ? 1 : 2) ? true : false
  const evaluationPriceIsExact: Equal<EvaluationPrice, 'open' | 'close'> = true
  const statusIsExact: Equal<PriceAlertStatus, 'active' | 'triggered' | 'dismissed'> = true
  const responsePriceIsExact: Equal<PriceAlertResponse['evaluationPrice'], EvaluationPrice> = true
  const responseStatusIsExact: Equal<PriceAlertResponse['status'], PriceAlertStatus> = true
  const triggerPriceIsExact: Equal<TriggerResponse['priceType'], EvaluationPrice> = true
  expect([evaluationPriceIsExact, statusIsExact, responsePriceIsExact, responseStatusIsExact, triggerPriceIsExact]).toEqual([true, true, true, true, true])
})
