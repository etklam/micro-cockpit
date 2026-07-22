import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { BrowserRouter } from 'react-router-dom'
import { expect, test } from 'vitest'
import App from '../App'
import { AuthProvider } from '../auth/AuthProvider'
import { I18nProvider } from '../i18n'
import type { TransactionResponse, TransactionWrite } from '../generated/edge'
import { utcToAccountDateTimeLocal } from '../features/accountTime'
import { server } from './setup'

const diaryId = 'diary-1'
const trade: TransactionResponse = {
  id: 'tx-1',
  diaryId,
  symbol: 'AAPL',
  side: 'buy',
  quantity: 2,
  price: 190.5,
  currency: 'USD',
  tradedAt: '2026-07-16T08:00:00.000Z',
  notes: 'Within plan',
  createdAt: '2026-07-16T08:00:00.000Z',
  updatedAt: '2026-07-16T08:00:00.000Z',
}

const bootstrap = {
  currentUser: { id: '11111111-1111-1111-1111-111111111111', email: 'owner@example.com', displayName: 'Owner' },
  timezone: 'Asia/Taipei',
  baseCurrency: 'USD',
  appearance: 'system', accentTheme: 'green', locale: 'en',
  role: 'user',
  accountType: 'human',
  currentLocalDate: '2026-07-16',
  availableProductAreas: ['today', 'diary', 'calendar'],
}

function authenticatedHandlers(options?: {
  items?: TransactionResponse[]
  onCreate?: (diary: string, body: TransactionWrite, key: string | null) => void
  onUpdate?: (diary: string, id: string, body: TransactionWrite) => void
  onDelete?: (diary: string, id: string) => void
  createStatus?: number
  updateStatus?: number
  deleteStatus?: number
}) {
  let items = [...(options?.items ?? [trade])]
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
    http.get('/api/app/tool-presets', () => HttpResponse.json({ items: [] })),
    http.get('/api/app/saved-calculations', () => HttpResponse.json({ items: [] })),
    http.get('/api/app/diaries/:id', () => HttpResponse.json({ id: diaryId, localDate: '2026-07-16', title: 'Direct entry', content: 'Notes', createdAt: '2026-07-16T00:00:00Z', updatedAt: '2026-07-16T00:00:00Z', tags: [] })),
    http.get('/api/app/diaries/:id/review', () => new HttpResponse(null, { status: 404 })),
    http.get('/api/app/diaries/:id/transactions', () => HttpResponse.json({ items })),
    http.post('/api/app/diaries/:id/transactions', async ({ params, request }) => {
      const body = await request.json() as TransactionWrite
      options?.onCreate?.(String(params.id), body, request.headers.get('Idempotency-Key'))
      if (options?.createStatus) return HttpResponse.json({ title: 'error' }, { status: options.createStatus })
      const created: TransactionResponse = {
        id: 'tx-new',
        diaryId: String(params.id),
        symbol: body.symbol,
        side: body.side,
        quantity: body.quantity,
        price: body.price,
        currency: body.currency,
        tradedAt: body.tradedAt,
        notes: body.notes ?? '',
        createdAt: '2026-07-16T09:00:00.000Z',
        updatedAt: '2026-07-16T09:00:00.000Z',
      }
      items = [...items, created]
      return HttpResponse.json(created, { status: 201 })
    }),
    http.put('/api/app/diaries/:diaryId/transactions/:id', async ({ params, request }) => {
      const body = await request.json() as TransactionWrite
      options?.onUpdate?.(String(params.diaryId), String(params.id), body)
      if (options?.updateStatus) return HttpResponse.json({ title: 'error' }, { status: options.updateStatus })
      items = items.map((item) => item.id === params.id ? {
        ...item,
        symbol: body.symbol,
        side: body.side,
        quantity: body.quantity,
        price: body.price,
        currency: body.currency,
        tradedAt: body.tradedAt,
        notes: body.notes ?? '',
        updatedAt: '2026-07-16T10:00:00.000Z',
      } : item)
      return new HttpResponse(null, { status: 204 })
    }),
    http.delete('/api/app/diaries/:diaryId/transactions/:id', ({ params }) => {
      options?.onDelete?.(String(params.diaryId), String(params.id))
      if (options?.deleteStatus) return HttpResponse.json({ title: 'error' }, { status: options.deleteStatus })
      items = items.filter((item) => item.id !== params.id)
      return new HttpResponse(null, { status: 204 })
    }),
  ]
}

function renderDiaryDetail() {
  window.history.replaceState({}, '', `/diary/${diaryId}`)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  render(<QueryClientProvider client={client}><BrowserRouter><AuthProvider><I18nProvider><App /></I18nProvider></AuthProvider></BrowserRouter></QueryClientProvider>)
  return client
}

test('edit populates the transaction form from the selected trade', async () => {
  server.use(...authenticatedHandlers())
  renderDiaryDetail()
  const user = userEvent.setup()

  expect(await screen.findByText('AAPL')).toBeInTheDocument()
  await user.click(screen.getByRole('button', { name: 'Edit AAPL trade' }))

  expect(screen.getByLabelText('Symbol')).toHaveValue('AAPL')
  expect(screen.getByLabelText('Side')).toHaveValue('buy')
  expect(screen.getByLabelText('Qty')).toHaveValue(2)
  expect(screen.getByLabelText('Price')).toHaveValue(190.5)
  expect(screen.getByLabelText('Currency')).toHaveValue('USD')
  expect(screen.getByLabelText('Traded at')).toHaveValue(utcToAccountDateTimeLocal(trade.tradedAt, 'Asia/Taipei'))
  expect(screen.getByLabelText('Notes')).toHaveValue('Within plan')
  expect(screen.getByRole('button', { name: 'Save changes' })).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Cancel' })).toBeInTheDocument()
})

test('opens profit/loss from a trade with transparent editable prefill', async () => {
  server.use(...authenticatedHandlers())
  renderDiaryDetail()
  const user = userEvent.setup()
  await user.click(await screen.findByRole('button', { name: 'P/L calculator' }))
  expect(await screen.findByRole('heading', { name: 'Profit / loss' })).toBeInTheDocument()
  expect(screen.getByText('Prefilled from AAPL trade. All values remain editable.')).toBeInTheDocument()
  expect(screen.getByLabelText('Entry price')).toHaveValue(190.5)
  expect(screen.getByLabelText('Quantity')).toHaveValue(2)
  await user.clear(screen.getByLabelText('Entry price'))
  await user.type(screen.getByLabelText('Entry price'), '200')
  expect(screen.getByLabelText('Entry price')).toHaveValue(200)
})

test('position-size result returns as a reviewable trade draft without submitting it', async () => {
  let created = false
  server.use(...authenticatedHandlers({ items: [], onCreate: () => { created = true } }))
  renderDiaryDetail()
  const user = userEvent.setup()
  await screen.findByText('No trades logged.')
  await user.type(screen.getByLabelText('Symbol'), 'aapl')
  await user.type(screen.getByLabelText('Price'), '100')
  await user.click(screen.getByRole('button', { name: 'Position size' }))
  expect(await screen.findByRole('heading', { name: 'Position size' })).toBeInTheDocument()
  expect(screen.getByLabelText('Entry price')).toHaveValue(100)
  await user.type(screen.getByLabelText('Account value'), '10000')
  await user.type(screen.getByLabelText('Risk %'), '1')
  await user.type(screen.getByLabelText('Stop price'), '95')
  await user.click(screen.getByRole('button', { name: 'Calculate position size' }))
  await user.click(screen.getByRole('button', { name: 'Create trade draft' }))
  expect(await screen.findByRole('heading', { name: 'Direct entry' })).toBeInTheDocument()
  expect(screen.getByLabelText('Symbol')).toHaveValue('AAPL')
  expect(screen.getByLabelText('Qty')).toHaveValue(20)
  expect(screen.getByLabelText('Price')).toHaveValue(100)
  expect((screen.getByLabelText('Notes') as HTMLInputElement).value).toContain('Review all values before saving.')
  expect(created).toBe(false)
})

test('calculation result opens an unpublished diary draft on the source date', async () => {
  server.use(...authenticatedHandlers())
  renderDiaryDetail()
  const user = userEvent.setup()
  await user.click(await screen.findByRole('button', { name: 'P/L calculator' }))
  await user.type(screen.getByLabelText('Exit or current price'), '200')
  await user.click(screen.getByRole('button', { name: 'Calculate P/L' }))
  await user.click(screen.getByRole('button', { name: 'Add to diary draft' }))
  expect(await screen.findByRole('heading', { name: 'Diary' })).toBeInTheDocument()
  expect(screen.getByDisplayValue('2026-07-16')).toBeInTheDocument()
  expect(screen.getByDisplayValue('Profit / loss — AAPL')).toBeInTheDocument()
  expect((screen.getByLabelText('Reflection') as HTMLTextAreaElement).value).toContain('Tool used: Profit / loss')
})

test('saving an edited trade calls update with the diary and transaction ids', async () => {
  let updated: { diaryId: string; id: string; body: TransactionWrite } | undefined
  server.use(...authenticatedHandlers({
    onUpdate: (updatedDiaryId, id, body) => { updated = { diaryId: updatedDiaryId, id, body } },
  }))
  renderDiaryDetail()
  const user = userEvent.setup()

  await user.click(await screen.findByRole('button', { name: 'Edit AAPL trade' }))
  await user.clear(screen.getByLabelText('Symbol'))
  await user.type(screen.getByLabelText('Symbol'), 'msft')
  await user.selectOptions(screen.getByLabelText('Side'), 'sell')
  await user.clear(screen.getByLabelText('Qty'))
  await user.type(screen.getByLabelText('Qty'), '3')
  await user.clear(screen.getByLabelText('Price'))
  await user.type(screen.getByLabelText('Price'), '210.25')
  await user.clear(screen.getByLabelText('Currency'))
  await user.type(screen.getByLabelText('Currency'), 'eur')
  await user.clear(screen.getByLabelText('Notes'))
  await user.type(screen.getByLabelText('Notes'), 'Corrected fill')
  await user.click(screen.getByRole('button', { name: 'Save changes' }))

  await waitFor(() => expect(updated).toMatchObject({
    diaryId,
    id: 'tx-1',
    body: {
      symbol: 'MSFT',
      side: 'sell',
      quantity: 3,
      price: 210.25,
      currency: 'EUR',
      notes: 'Corrected fill',
    },
  }))
  expect(updated?.body.tradedAt).toMatch(/^\d{4}-\d{2}-\d{2}T/)
})

test('successful update exits edit mode and refreshes the list', async () => {
  server.use(...authenticatedHandlers())
  renderDiaryDetail()
  const user = userEvent.setup()

  await user.click(await screen.findByRole('button', { name: 'Edit AAPL trade' }))
  await user.clear(screen.getByLabelText('Symbol'))
  await user.type(screen.getByLabelText('Symbol'), 'nvda')
  await user.click(screen.getByRole('button', { name: 'Save changes' }))

  expect(await screen.findByText('NVDA')).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Add' })).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Save changes' })).not.toBeInTheDocument()
  expect(screen.getByLabelText('Symbol')).toHaveValue('')
  expect(screen.getByLabelText('Qty')).toHaveValue(null)
})

test('cancel restores create mode without changing data', async () => {
  let updated = false
  server.use(...authenticatedHandlers({
    onUpdate: () => { updated = true },
  }))
  renderDiaryDetail()
  const user = userEvent.setup()

  await user.click(await screen.findByRole('button', { name: 'Edit AAPL trade' }))
  await user.clear(screen.getByLabelText('Symbol'))
  await user.type(screen.getByLabelText('Symbol'), 'tsla')
  await user.click(screen.getByRole('button', { name: 'Cancel' }))

  expect(screen.getByRole('button', { name: 'Add' })).toBeInTheDocument()
  expect(screen.getByLabelText('Symbol')).toHaveValue('')
  expect(screen.getByText('AAPL')).toBeInTheDocument()
  expect(updated).toBe(false)
})

test('update failure preserves form values and shows an error', async () => {
  server.use(...authenticatedHandlers({ updateStatus: 400 }))
  renderDiaryDetail()
  const user = userEvent.setup()

  await user.click(await screen.findByRole('button', { name: 'Edit AAPL trade' }))
  await user.clear(screen.getByLabelText('Symbol'))
  await user.type(screen.getByLabelText('Symbol'), 'ibm')
  await user.click(screen.getByRole('button', { name: 'Save changes' }))

  expect(await screen.findByRole('alert')).toHaveTextContent('Check the symbol, quantity, price, currency, and trade time.')
  expect(screen.getByLabelText('Symbol')).toHaveValue('IBM')
  expect(screen.getByRole('button', { name: 'Save changes' })).toBeInTheDocument()
})

test('deleting the trade currently being edited clears edit mode', async () => {
  server.use(...authenticatedHandlers())
  renderDiaryDetail()
  const user = userEvent.setup()

  await user.click(await screen.findByRole('button', { name: 'Edit AAPL trade' }))
  expect(screen.getByRole('button', { name: 'Save changes' })).toBeInTheDocument()
  await user.click(screen.getByRole('button', { name: 'Delete trade' }))
  const dialog = await screen.findByRole('alertdialog')
  await user.click(within(dialog).getByRole('button', { name: 'Delete' }))

  expect(await screen.findByText('No trades logged.')).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Add' })).toBeInTheDocument()
  expect(screen.getByLabelText('Symbol')).toHaveValue('')
})

test('create still posts a new trade for the diary', async () => {
  let created: { diaryId: string; body: TransactionWrite; key: string | null } | undefined
  server.use(...authenticatedHandlers({
    items: [],
    onCreate: (createdDiaryId, body, key) => { created = { diaryId: createdDiaryId, body, key } },
  }))
  renderDiaryDetail()
  const user = userEvent.setup()

  expect(await screen.findByText('No trades logged.')).toBeInTheDocument()
  await user.type(screen.getByLabelText('Symbol'), 'aapl')
  await user.type(screen.getByLabelText('Qty'), '1.5')
  await user.type(screen.getByLabelText('Price'), '100')
  await user.type(screen.getByLabelText('Notes'), 'First fill')
  await user.click(screen.getByRole('button', { name: 'Add' }))

  await waitFor(() => expect(created).toMatchObject({
    diaryId,
    body: { symbol: 'AAPL', side: 'buy', quantity: 1.5, price: 100, currency: 'USD', notes: 'First fill' },
  }))
  expect(created?.key).toBeTruthy()
  expect(await screen.findByText('AAPL')).toBeInTheDocument()
})

test('delete still removes a trade after confirmation', async () => {
  let deleted: { diaryId: string; id: string } | undefined
  server.use(...authenticatedHandlers({
    onDelete: (deletedDiaryId, id) => { deleted = { diaryId: deletedDiaryId, id } },
  }))
  renderDiaryDetail()
  const user = userEvent.setup()

  await user.click(await screen.findByRole('button', { name: 'Delete trade' }))
  expect(deleted).toBeUndefined()
  const dialog = await screen.findByRole('alertdialog')
  await user.click(within(dialog).getByRole('button', { name: 'Delete' }))

  await waitFor(() => expect(deleted).toEqual({ diaryId, id: 'tx-1' }))
  expect(await screen.findByText('No trades logged.')).toBeInTheDocument()
})
