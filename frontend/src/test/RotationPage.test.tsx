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
  availableProductAreas: ['rotation'],
}

const universes = {
  items: [
    { id: '55555555-5555-5555-5555-555555555555', code: 'SECTORS', name: 'US sectors', rankScope: 'sector', createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z' },
    { id: '66666666-6666-6666-6666-666666666666', code: 'ASSETS', name: 'Global assets', rankScope: 'universe', createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z' },
  ],
}

const monitor = {
  universe: { id: universes.items[0].id, code: 'SECTORS', name: 'US sectors', rankScope: 'sector' },
  snapshotDate: '2026-07-15', formulaVersion: 'rotation-v1', status: 'ok',
  marketState: { state: 'risk_on', breadthPercent: 62.5, benchmarkAboveMa200: true, status: 'ok' },
  sectorBreadth: [
    { sector: 'Technology', memberCount: 2, availableCount: 2, aboveMa20Percent: 100, aboveMa50Percent: 50, aboveMa200Percent: 50, status: 'ok' },
    { sector: 'Energy', memberCount: 2, availableCount: 2, aboveMa20Percent: 0, aboveMa50Percent: 50, aboveMa200Percent: null, status: 'insufficient_data' },
  ],
  etfs: [
    { symbol: 'XLE', label: 'Energy', sector: 'Energy', close: 88.1, return2w: -2.5, return1m: 1.1, return3m: 3.2, rank2w: 1, rankGroup: 'Energy', percentile2w: 0, aboveMa20: false, aboveMa50: true, aboveMa200: null, status: 'ok' },
    { symbol: 'XLK', label: 'Technology', sector: 'Technology', close: 240.5, return2w: 4.5, return1m: 8.1, return3m: 12.2, rank2w: 1, rankGroup: 'Technology', percentile2w: 1, aboveMa20: true, aboveMa50: true, aboveMa200: true, status: 'ok' },
  ],
}

const universeMonitor = {
  ...monitor,
  universe: { id: universes.items[1].id, code: 'ASSETS', name: 'Global assets', rankScope: 'universe' },
  etfs: [
    { ...monitor.etfs[0], rank2w: 2, rankGroup: 'ASSETS' },
    { ...monitor.etfs[1], rank2w: 1, rankGroup: 'ASSETS' },
  ],
}

function handlers(options?: { bootstrap?: unknown; universes?: unknown; monitor?: unknown; monitorStatus?: number; onMonitor?: (url: URL) => void }) {
  return [
    http.post('/api/auth/refresh', () => HttpResponse.json({ accessToken: 'memory-only-token', expiresAt: '2026-07-16T12:00:00Z' })),
    http.get('/api/app/bootstrap', () => HttpResponse.json(options?.bootstrap ?? bootstrap)),
    http.get('/api/app/rotation/universes', () => HttpResponse.json(options?.universes ?? universes)),
    http.get('/api/app/rotation/monitor', ({ request }) => {
      const url = new URL(request.url)
      options?.onMonitor?.(url)
      return HttpResponse.json(options?.monitor ?? (url.searchParams.get('universe') === 'ASSETS' ? universeMonitor : monitor), { status: options?.monitorStatus ?? 200 })
    }),
  ]
}

function renderRotation(path = '/rotation?universe=SECTORS&scope=sector') {
  window.history.replaceState({}, '', path)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(<QueryClientProvider client={client}><BrowserRouter><AuthProvider><App /></AuthProvider></BrowserRouter></QueryClientProvider>)
  return client
}

test('rotation dashboard renders existing sector snapshot fields by rank group', async () => {
  server.use(...handlers())
  renderRotation()

  expect(await screen.findByRole('heading', { name: 'Market rotation' })).toBeInTheDocument()
  expect(await screen.findByText(/2026-07-15/)).toBeInTheDocument()
  expect(screen.getAllByText('US sectors').length).toBeGreaterThan(0)
  expect(screen.getAllByText('Risk On').length).toBeGreaterThan(0)
  const rows = await screen.findAllByRole('row')
  expect(within(rows[1]).getByText('XLE')).toBeInTheDocument()
  expect(within(rows[2]).getByText('XLK')).toBeInTheDocument()
})

test('sector scope explains independent ranks without global strongest groups', async () => {
  server.use(...handlers())
  renderRotation()

  expect(await screen.findByText('Ranked independently within each sector')).toBeInTheDocument()
  expect(screen.getByText('2 sectors')).toBeInTheDocument()
  expect(screen.getByText('2 available rows')).toBeInTheDocument()
  expect(screen.queryByText('Strongest groups')).not.toBeInTheDocument()
  expect(screen.queryByText('Weakest groups')).not.toBeInTheDocument()
})

test('sector rows stay grouped and rank labels include their rank group', async () => {
  const grouped = {
    ...monitor,
    etfs: [
      { ...monitor.etfs[1], symbol: 'SOXX', label: 'Semiconductors', rank2w: 2 },
      monitor.etfs[0],
      { ...monitor.etfs[0], symbol: 'XOP', label: 'Oil and gas', rank2w: 2 },
      monitor.etfs[1],
    ],
  }
  server.use(...handlers({ monitor: grouped }))
  renderRotation()

  const rows = await screen.findAllByRole('row')
  expect(within(rows[1]).getByText('XLE')).toBeInTheDocument()
  expect(within(rows[2]).getByText('XOP')).toBeInTheDocument()
  expect(within(rows[3]).getByText('XLK')).toBeInTheDocument()
  expect(within(rows[4]).getByText('SOXX')).toBeInTheDocument()
  expect(screen.getByText('#1 in Energy')).toBeInTheDocument()
  expect(screen.getByText('#1 in Technology')).toBeInTheDocument()
})

test('universe scope leaders follow global backend rank', async () => {
  server.use(...handlers())
  renderRotation('/rotation?universe=ASSETS&scope=universe')

  expect(await screen.findByText('Leaders')).toBeInTheDocument()
  expect(screen.getByText('Technology, Energy')).toBeInTheDocument()
  expect(screen.getAllByText('By global backend 2-week rank')).toHaveLength(2)
  expect(screen.queryByText('Ranked independently within each sector')).not.toBeInTheDocument()
})

test('percentiles are formatted from the raw zero-to-one contract', async () => {
  const percentiles = {
    ...monitor,
    etfs: [
      { ...monitor.etfs[0], percentile2w: null },
      { ...monitor.etfs[1], percentile2w: 0.625 },
    ],
  }
  server.use(...handlers({ monitor: percentiles }))
  renderRotation()

  const rows = await screen.findAllByRole('row')
  expect(within(rows[1]).getAllByRole('cell')[7]).toHaveTextContent('—')
  expect(within(rows[2]).getAllByRole('cell')[7]).toHaveTextContent('62.5%')
})

test('freshness reports objective calendar age without a seven-day threshold', async () => {
  server.use(...handlers({ bootstrap: { ...bootstrap, currentLocalDate: '2026-07-30' } }))
  renderRotation()

  expect(await screen.findByText('Age: 15 calendar days')).toBeInTheDocument()
  expect(screen.queryByText(/Stale snapshot|Current snapshot/)).not.toBeInTheDocument()
})

test('URL filters persist and invalid values fall back safely', async () => {
  server.use(...handlers())
  renderRotation('/rotation?universe=SECTORS&scope=sector&group=Energy&ma=above50')

  expect(await screen.findByDisplayValue('US sectors')).toBeInTheDocument()
  expect(screen.getByDisplayValue('Energy')).toBeInTheDocument()
  expect(screen.getByDisplayValue('Above MA50')).toBeInTheDocument()
  expect(window.location.search).toContain('group=Energy')

  window.history.replaceState({}, '', '/rotation?universe=UNKNOWN&scope=invalid&ma=broken&sort=rsi')
  window.dispatchEvent(new PopStateEvent('popstate'))
  await waitFor(() => expect(window.location.search).toContain('universe=SECTORS'))
  expect(window.location.search).toContain('scope=sector')
  expect(window.location.search).not.toContain('ma=broken')
  expect(window.location.search).not.toContain('sort=rsi')
})

test('browser back and forward restore rotation filters', async () => {
  server.use(...handlers())
  renderRotation()
  const user = userEvent.setup()
  const group = await screen.findByLabelText('Group')

  await user.selectOptions(group, 'Energy')
  await waitFor(() => expect(window.location.search).toContain('group=Energy'))
  await user.selectOptions(group, 'Technology')
  await waitFor(() => expect(window.location.search).toContain('group=Technology'))

  window.history.back()
  await waitFor(() => expect(screen.getByLabelText('Group')).toHaveValue('Energy'))
  expect(window.location.search).toContain('group=Energy')

  window.history.forward()
  await waitFor(() => expect(screen.getByLabelText('Group')).toHaveValue('Technology'))
  expect(window.location.search).toContain('group=Technology')
})

test('null performance values sort last within their sector partition', async () => {
  const withNull = { ...monitor, etfs: [...monitor.etfs, { ...monitor.etfs[0], symbol: 'XOP', label: 'Oil and gas', rank2w: 2, return2w: null }] }
  server.use(...handlers({ monitor: withNull }))
  renderRotation('/rotation?universe=SECTORS&scope=sector&sort=return2w&direction=desc')

  const rows = await screen.findAllByRole('row')
  expect(within(rows[1]).getByText('XLE')).toBeInTheDocument()
  expect(within(rows[2]).getByText('XOP')).toBeInTheDocument()
  expect(within(rows[3]).getByText('XLK')).toBeInTheDocument()
})

test('changing universe uses a parameterized query key and keeps both cache entries', async () => {
  const requests: string[] = []
  server.use(...handlers({ onMonitor: url => requests.push(url.searchParams.get('universe') ?? '') }))
  const client = renderRotation()
  const user = userEvent.setup()
  await screen.findByText(/2026-07-15/)

  await user.selectOptions(screen.getByLabelText('Universe'), 'ASSETS')
  await waitFor(() => expect(requests).toContain('ASSETS'))
  expect(client.getQueryData(['rotation', 'monitor', 'SECTORS', 'sector'])).toBeDefined()
  expect(client.getQueryData(['rotation', 'monitor', 'ASSETS', 'universe'])).toBeDefined()
  expect(window.location.search).toContain('universe=ASSETS')
  expect(window.location.search).toContain('scope=universe')
})

test('empty universe has a valid empty state', async () => {
  server.use(...handlers({ universes: { items: [] } }))
  renderRotation()
  expect(await screen.findByText('No rotation universes configured')).toBeInTheDocument()
})

test('backend unavailable is not presented as empty data', async () => {
  server.use(...handlers({ monitorStatus: 503 }))
  renderRotation()
  expect(await screen.findByRole('heading', { name: 'Rotation data unavailable' })).toBeInTheDocument()
  expect(screen.queryByRole('heading', { name: 'No rotation universes configured' })).not.toBeInTheDocument()
})
