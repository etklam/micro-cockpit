import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { BrowserRouter } from 'react-router-dom'
import { expect, test, vi } from 'vitest'
import App from '../App'
import { AuthProvider } from '../auth/AuthProvider'
import { I18nProvider } from '../i18n'
import { partnerCompareErrorKind } from '../features/api'
import { ApiError } from '../generated/edge'
import { defaultRangeFromLocalDate } from '../screens/partners'
import { server } from './setup'

const bootstrap = {
  currentUser: { id: '11111111-1111-1111-1111-111111111111', email: 'owner@example.com', displayName: 'Owner' },
  timezone: 'Asia/Taipei',
  baseCurrency: 'USD',
  appearance: 'system', accentTheme: 'green',
  locale: 'en',
  role: 'user',
  accountType: 'human',
  currentLocalDate: '2026-07-16',
  availableProductAreas: ['partners'],
}

const partner = {
  id: 'link-1',
  otherUserId: '22222222-2222-2222-2222-222222222222',
  partnerType: 'human',
  status: 'accepted',
  createdAt: '2026-07-01T00:00:00Z',
  updatedAt: '2026-07-01T00:00:00Z',
  initiatedByMe: true,
  myShareDiaries: true,
  partnerShareDiaries: true,
  partnerDisplayName: 'Alex',
}

const invite = {
  id: 'inv-1',
  status: 'pending',
  expiresAt: '2026-07-23T00:00:00Z',
  createdAt: '2026-07-16T00:00:00Z',
}

const compareOk = {
  linkId: 'link-1',
  partnerDisplayName: 'Alex',
  partnerUserId: partner.otherUserId,
  from: '2026-06-17',
  to: '2026-07-16',
  days: [
    {
      localDate: '2026-07-15',
      mine: [{ id: 'd1', localDate: '2026-07-15', title: 'Mine day', content: 'Bought **AAPL**', tags: ['plan'] }],
      partner: [{ id: 'd2', localDate: '2026-07-15', title: 'Partner day', content: 'Held cash', tags: [] }],
    },
  ],
  capabilities: { partnerDiaries: 'available' as const },
}

function handlers(options?: {
  partners?: unknown
  partnersStatus?: number
  invitations?: unknown
  invitationsStatus?: number
  compare?: unknown
  compareStatus?: number
  compareBody?: string
  onCreateInvite?: () => void
  createInviteCount?: { n: number }
  onCompare?: (url: URL) => void
}) {
  return [
    http.post('/api/auth/refresh', () => HttpResponse.json({ accessToken: 'memory-token', expiresAt: '2026-07-16T12:00:00Z' })),
    http.get('/api/app/bootstrap', () => HttpResponse.json(bootstrap)),
    http.get('/api/app/partners', () =>
      HttpResponse.json(options?.partners ?? { items: [partner] }, { status: options?.partnersStatus ?? 200 })),
    http.get('/api/app/partners/invitations', () =>
      HttpResponse.json(options?.invitations ?? { items: [invite] }, { status: options?.invitationsStatus ?? 200 })),
    http.post('/api/app/partners/invitations', () => {
      options?.onCreateInvite?.()
      if (options?.createInviteCount) options.createInviteCount.n += 1
      return HttpResponse.json({ id: 'inv-new', code: 'CODE-ONCE', expiresAt: '2026-07-23T00:00:00Z' })
    }),
    http.delete('/api/app/partners/invitations/:id', () => new HttpResponse(null, { status: 204 })),
    http.post('/api/app/partners/invitations/redeem', () =>
      HttpResponse.json({ linkId: 'link-2', status: 'pending' })),
    http.post('/api/app/partners/:id/accept', () => HttpResponse.json({ ...partner, status: 'accepted' })),
    http.delete('/api/app/partners/:id', () => new HttpResponse(null, { status: 204 })),
    http.put('/api/app/partners/:id/share-policy', async ({ request }) => {
      const body = await request.json() as { shareDiaries: boolean }
      return HttpResponse.json({ ...partner, myShareDiaries: body.shareDiaries })
    }),
    http.get('/api/app/partners/:linkId/compare', ({ request }) => {
      options?.onCompare?.(new URL(request.url))
      if (options?.compareStatus && options.compareStatus !== 200) {
        return new HttpResponse(options.compareBody ?? '', { status: options.compareStatus })
      }
      return HttpResponse.json(options?.compare ?? compareOk)
    }),
    http.post('/api/app/agents', () =>
      HttpResponse.json({ userId: 'a1', keyId: 'k1', apiKey: 'agent-secret', scopes: ['research:read'] })),
  ]
}

function renderPath(path: string) {
  window.history.replaceState({}, '', path)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <BrowserRouter>
        <AuthProvider>
          <I18nProvider>
            <App />
          </I18nProvider>
        </AuthProvider>
      </BrowserRouter>
    </QueryClientProvider>,
  )
  return client
}

test('default 30-day range is calendar math on bootstrap.currentLocalDate', () => {
  expect(defaultRangeFromLocalDate('2026-07-16')).toEqual({ from: '2026-06-17', to: '2026-07-16' })
  expect(defaultRangeFromLocalDate('2026-03-01')).toEqual({ from: '2026-01-31', to: '2026-03-01' })
  expect(defaultRangeFromLocalDate('bad')).toEqual({ from: '', to: '' })
})

test('partnerCompareErrorKind keeps 404 non-disclosing and distinguishes auth/invalid', () => {
  expect(partnerCompareErrorKind(new ApiError(404, ''))).toBe('missing')
  expect(partnerCompareErrorKind(new ApiError(404, '{"detail":"revoked"}'))).toBe('missing')
  expect(partnerCompareErrorKind(new ApiError(403, ''))).toBe('auth')
  expect(partnerCompareErrorKind(new ApiError(401, ''))).toBe('auth')
  expect(partnerCompareErrorKind(new ApiError(400, 'invalid_date_range'))).toBe('invalid_range')
  expect(partnerCompareErrorKind(new ApiError(503, ''))).toBe('unavailable')
})

test('partners list loads independently when invitations fail', async () => {
  server.use(...handlers({ invitationsStatus: 503 }))
  renderPath('/partners')
  expect(await screen.findByText('Alex')).toBeInTheDocument()
  expect(screen.getByText(/reach the cockpit/i)).toBeInTheDocument()
  expect(screen.getByRole('button', { name: /try again/i })).toBeInTheDocument()
})

test('invitations load when partners list fails', async () => {
  server.use(...handlers({ partnersStatus: 503 }))
  renderPath('/partners')
  expect(await screen.findByText('Unused invitations')).toBeInTheDocument()
  expect(screen.getByText(/reach the cockpit/i)).toBeInTheDocument()
  // open invitation still shown
  expect(screen.getByRole('button', { name: 'Revoke invitation' })).toBeInTheDocument()
})

test('create invitation guards double submit', async () => {
  const counter = { n: 0 }
  server.use(
    ...handlers({ createInviteCount: counter }),
    http.post('/api/app/partners/invitations', async () => {
      counter.n += 1
      await new Promise(r => setTimeout(r, 40))
      return HttpResponse.json({ id: 'inv-new', code: 'CODE-ONCE', expiresAt: '2026-07-23T00:00:00Z' })
    }),
  )
  renderPath('/partners')
  const user = userEvent.setup()
  const btn = await screen.findByRole('button', { name: 'Create invitation code' })
  await user.click(btn)
  await user.click(btn)
  await waitFor(() => expect(screen.getByText('CODE-ONCE')).toBeInTheDocument())
  expect(counter.n).toBe(1)
})

test('copy invitation falls back when clipboard rejects', async () => {
  server.use(...handlers())
  renderPath('/partners')
  const user = userEvent.setup()
  await user.click(await screen.findByRole('button', { name: 'Create invitation code' }))
  expect(await screen.findByText('CODE-ONCE')).toBeInTheDocument()

  const writeText = vi.fn().mockRejectedValue(new Error('denied'))
  Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText } })
  document.execCommand = vi.fn().mockReturnValue(true) as typeof document.execCommand

  await user.click(screen.getByRole('button', { name: 'Copy code' }))
  await waitFor(() => expect(writeText).toHaveBeenCalledWith('CODE-ONCE'))
  expect(document.execCommand).toHaveBeenCalledWith('copy')
  expect(screen.queryByText('Could not copy the invitation code. Copy it manually.')).not.toBeInTheDocument()
})

test('compare defaults to bootstrap local 30-day range', async () => {
  let seen = ''
  server.use(...handlers({ onCompare: url => { seen = url.search } }))
  renderPath('/partners/link-1/compare')
  expect(await screen.findByText('With Alex')).toBeInTheDocument()
  await waitFor(() => expect(seen).toContain('from=2026-06-17'))
  expect(seen).toContain('to=2026-07-16')
  expect(screen.getByLabelText('From')).toHaveValue('2026-06-17')
  expect(screen.getByLabelText('To')).toHaveValue('2026-07-16')
})

test('compare preserves valid URL params', async () => {
  let seen = ''
  server.use(...handlers({ onCompare: url => { seen = url.search } }))
  renderPath('/partners/link-1/compare?from=2026-07-01&to=2026-07-10')
  expect(await screen.findByLabelText('From')).toHaveValue('2026-07-01')
  expect(screen.getByLabelText('To')).toHaveValue('2026-07-10')
  await waitFor(() => expect(seen).toContain('from=2026-07-01'))
  expect(seen).toContain('to=2026-07-10')
})

test('invalid date params show translated state and skip compare request', async () => {
  let calls = 0
  server.use(...handlers({ onCompare: () => { calls += 1 } }))
  renderPath('/partners/link-1/compare?from=2026-02-31&to=2026-07-10')
  expect(await screen.findByRole('alert')).toHaveTextContent('Enter a valid date range.')
  expect(screen.getAllByText('Enter a valid date range.').length).toBeGreaterThanOrEqual(1)
  expect(calls).toBe(0)
})

test.each([
  [404, '', 'This partnership was not found or is no longer active.'],
  [403, '', 'You are not allowed to open this compare view.'],
  [503, '', 'This compare view is unavailable.'],
] as const)('compare status %s shows distinct state', async (status, body, message) => {
  server.use(...handlers({ compareStatus: status, compareBody: body }))
  renderPath('/partners/link-1/compare')
  expect(await screen.findByText(message)).toBeInTheDocument()
  expect(screen.getByRole('button', { name: /try again/i })).toBeInTheDocument()
})

test('compare renders markdown read-only and empty name fallback', async () => {
  server.use(...handlers({
    compare: {
      ...compareOk,
      partnerDisplayName: '',
      days: [{
        localDate: '2026-07-15',
        mine: [{ id: 'd1', localDate: '2026-07-15', title: '', content: 'Bought **AAPL**', tags: [] }],
        partner: [],
      }],
      capabilities: { partnerDiaries: 'not_shared' },
    },
  }))
  renderPath('/partners/link-1/compare')
  expect(await screen.findByText('Not shared')).toBeInTheDocument()
  expect(screen.getByText('With Partner')).toBeInTheDocument()
  // markdown bold rendered, no contenteditable partner controls
  expect(screen.getByText('AAPL').tagName).toBe('STRONG')
  expect(document.querySelector('[contenteditable]')).toBeNull()
  expect(document.querySelector('textarea')).toBeNull()
})

test('nullable partner display name falls back on list', async () => {
  server.use(...handlers({
    partners: { items: [{ ...partner, partnerDisplayName: '' }] },
  }))
  renderPath('/partners')
  const card = await screen.findByText('Share my diaries with this partner')
  expect(within(card.closest('.partner-card') as HTMLElement).getByText('Partner')).toBeInTheDocument()
})
