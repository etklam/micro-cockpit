import { useEffect, useMemo, useState } from 'react'
import type { FormEvent, ReactNode } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { priceAlertCreateErrorKind, type PriceAlert, type PriceAlertEvaluationPrice, type RotationItem, type WatchlistItem } from './features/api'
import {
  useAddPriceAlertMutation, useAddWatchlistMutation, useArticleQuery, useArticlesQuery,
  useCreateAgentMutation, useDeletePriceAlertMutation, useDismissPriceAlertMutation, usePartnersQuery,
  usePriceAlertTriggersQuery, usePriceAlertsQuery, useRemoveWatchlistMutation, useResearchNoteQuery, useResearchTimelineQuery,
  useBootstrapQuery, useReactivatePriceAlertMutation, useRotationQuery, useRotationUniversesQuery, useSaveResearchNoteMutation, useWatchlistQuery,
} from './features/queries'
import { calculateTool, isToolId, type ToolId } from './features/toolsCalc'
import { Badge, Button, Card, EmptyBox, Field, IconButton, PageHeader, SelectBox, Stat, TextArea, TextInput } from './ui'
import { PageSkeleton, SectionError, useCockpit } from './shell'
import type { Page } from './shell'

const ListState = ({ loading, error, empty, retry, children }: { loading: boolean; error?: string; empty: boolean; retry: () => void; children: ReactNode }) =>
  loading ? <PageSkeleton rows={2} /> : error ? <SectionError onRetry={retry} /> : empty ? <EmptyBox title="Nothing here yet" hint="Add the first item when it becomes useful." /> : <>{children}</>

export function MorePage() {
  const { go } = useCockpit()
  const links: { page: Page; title: string; hint: string }[] = [
    { page: 'review', title: 'Monthly review', hint: 'Review process and outcome side by side.' },
    { page: 'alerts', title: 'Diary alerts', hint: 'Return to reflections at the right time.' },
    { page: 'settings', title: 'Settings', hint: 'Timezone, currency, language, appearance, and display name.' },
    { page: 'watchlist', title: 'Watchlist', hint: 'Research symbols and keep a decision trail.' },
    { page: 'price-alerts', title: 'Price alerts', hint: 'Track levels that deserve attention.' },
    { page: 'rotation', title: 'Market rotation', hint: 'See relative group momentum.' },
    { page: 'partners', title: 'Partners', hint: 'Keep useful market relationships close.' },
    { page: 'articles', title: 'Articles', hint: 'Read the latest saved research.' },
    { page: 'tools', title: 'Tools', hint: 'Small calculators for position decisions.' },
  ]
  return <><PageHeader title="More" subtitle="Research and decision support" /><div className="feature-grid">{links.map(x => <button className="feature-link card" key={x.page} onClick={() => go(x.page)}><strong>{x.title}</strong><span>{x.hint}</span></button>)}</div></>
}

export function WatchlistPage() {
  const list = useWatchlistQuery()
  const [symbol, setSymbol] = useState('')
  const [selected, setSelected] = useState<WatchlistItem | null>(null)
  const addWatchlist = useAddWatchlistMutation()
  const removeWatchlist = useRemoveWatchlistMutation()
  async function add(e: FormEvent) { e.preventDefault(); if (!symbol.trim()) return; await addWatchlist.mutateAsync(symbol.trim().toUpperCase()); setSymbol('') }
  async function remove(id: string) { await removeWatchlist.mutateAsync(id); if (selected?.stock.id === id) setSelected(null) }
  const items = list.data?.items ?? []
  return <><PageHeader title="Watchlist" subtitle="Research before action" />
    <Card flush><form className="inline-form-wrap inline-form" onSubmit={add}><TextInput aria-label="Symbol" required maxLength={16} value={symbol} onChange={e => setSymbol(e.target.value)} placeholder="Symbol, e.g. NVDA" /><Button variant="primary" icon="plus" type="submit" loading={addWatchlist.isPending}>Watch</Button></form></Card>
    <ListState loading={list.isLoading} error={list.isError ? 'error' : undefined} empty={!items.length} retry={() => { void list.refetch() }}><div className="research-layout"><ul className="compact-list">{items.map(item => <li key={item.stock.id}><Card className={selected?.stock.id === item.stock.id ? 'is-selected' : ''}><button className="row-main" onClick={() => setSelected(item)}><strong>{item.stock.symbol}</strong><span>{item.stock.name || item.currentNote || 'Open research'}</span></button><IconButton icon="trash" label={`Remove ${item.stock.symbol}`} onClick={() => remove(item.stock.id)} /></Card></li>)}</ul>{selected ? <Research key={selected.stock.id} item={selected} /> : <Card><EmptyBox title="Choose a symbol" hint="Notes and its timeline will appear here." dense /></Card>}</div></ListState>
  </>
}

function Research({ item }: { item: WatchlistItem }) {
  const note = useResearchNoteQuery(item.stock.id)
  const timeline = useResearchTimelineQuery(item.stock.id)
  const [content, setContent] = useState(item.currentNote ?? '')
  const saveNote = useSaveResearchNoteMutation(item.stock.id)
  async function save(e: FormEvent) { e.preventDefault(); await saveNote.mutateAsync(content.trim()) }
  return <Card className="research"><h2>{item.stock.symbol} research</h2><form onSubmit={save} className="stack"><Field label="Research note"><TextArea value={content} onChange={e => setContent(e.target.value)} placeholder="What changed your thesis?" /></Field><Button variant="primary" type="submit" loading={saveNote.isPending}>Save note</Button></form>
    <p className="is-muted">{note.isLoading ? 'Loading note…' : note.isError ? 'Could not load the note.' : note.data ? `Last updated ${new Date(note.data.updatedAt).toLocaleString()}` : 'No saved note yet.'}</p>
    <h3>Timeline</h3>{timeline.isLoading ? <p className="is-muted">Loading timeline…</p> : timeline.isError ? <SectionError onRetry={() => { void timeline.refetch() }} /> : !timeline.data?.items.length ? <p className="is-muted">No activity yet.</p> : <ul className="timeline">{timeline.data.items.map(x => <li key={x.id}><time>{new Date(x.eventTime).toLocaleString()} · {x.sourceType}</time><strong>{x.title}</strong>{x.content ? <p>{x.content}</p> : null}</li>)}</ul>}
  </Card>
}

export function PriceAlertsPage() {
  const result = usePriceAlertsQuery()
  const [symbol, setSymbol] = useState('')
  const [price, setPrice] = useState('')
  const [direction, setDirection] = useState('above')
  const [evaluationPrice, setEvaluationPrice] = useState<PriceAlertEvaluationPrice>('close')
  const [historyAlertId, setHistoryAlertId] = useState<string | null>(null)
  const addPriceAlert = useAddPriceAlertMutation()
  const deletePriceAlert = useDeletePriceAlertMutation()
  const dismissPriceAlert = useDismissPriceAlertMutation()
  const reactivatePriceAlert = useReactivatePriceAlertMutation()
  async function add(e: FormEvent) {
    e.preventDefault()
    try {
      await addPriceAlert.mutateAsync({ symbol: symbol.trim().toUpperCase(), threshold: Number(price), condition: direction, evaluationPrice })
      setSymbol('')
      setPrice('')
    } catch {
      // The mutation state renders a safe, actionable error message.
    }
  }
  async function remove(x: PriceAlert) { await deletePriceAlert.mutateAsync(x.id) }
  const items = result.data?.items ?? []
  const createError = addPriceAlert.error ? ({
    no_published_price: 'Symbol has no published daily price.',
    invalid_request: 'Invalid alert request.',
    unavailable: 'Market data unavailable.',
    timeout: 'Price alert request timed out.',
    unknown: 'Could not create the price alert.',
  } as const)[priceAlertCreateErrorKind(addPriceAlert.error)] : null
  return <>
    <PageHeader title="Price alerts" subtitle="Daily-bar levels worth reviewing" />
    <Card flush><form className="alert-form__body" onSubmit={add}><div className="form-row">
      <Field label="Symbol"><TextInput required value={symbol} onChange={e => setSymbol(e.target.value)} /></Field>
      <Field label="Direction"><SelectBox value={direction} onChange={e => setDirection(e.target.value)}><option value="above">Above</option><option value="below">Below</option></SelectBox></Field>
      <Field label="Target price"><TextInput required min="0.0001" step="any" type="number" value={price} onChange={e => setPrice(e.target.value)} /></Field>
      <Field label="Evaluate using" hint="Evaluated after the daily bar is published."><SelectBox aria-label="Evaluate using" value={evaluationPrice} onChange={e => setEvaluationPrice(e.target.value as PriceAlertEvaluationPrice)}><option value="close">Close</option><option value="open">Open</option></SelectBox></Field>
    </div>{createError ? <p role="alert" className="form-error">{createError}</p> : null}<div className="form-actions"><Button variant="primary" type="submit" loading={addPriceAlert.isPending}>Create alert</Button></div></form></Card>
    {result.isLoading ? <PageSkeleton rows={2} /> : result.isError ? <SectionError onRetry={() => { void result.refetch() }} /> : !items.length ? <EmptyBox title="No price alerts" hint="Create an alert to evaluate the next published daily bar." /> : <ul className="compact-list">{items.map(x => <li key={x.id}><Card>
      <div className="row-main"><strong>{x.symbol}</strong><span>{x.conditionType === 'above' ? 'Above' : 'Below'} {x.threshold.toLocaleString()}</span><span>Evaluate using {x.evaluationPrice === 'open' ? 'Open' : 'Close'}</span><span>{x.lastEvaluatedDate ? `Last evaluated ${x.lastEvaluatedDate}` : 'Not evaluated yet'}</span>{x.baselineClose == null ? null : <span>Baseline close {x.baselineClose.toLocaleString()}</span>}</div>
      <Badge tone={x.status === 'triggered' ? 'warn' : 'primary'}>{x.status}</Badge>
      {x.status === 'dismissed' ? <Button size="sm" onClick={() => reactivatePriceAlert.mutate(x.id)} aria-label={`Reactivate ${x.symbol} alert`}>Reactivate</Button> : <Button size="sm" onClick={() => dismissPriceAlert.mutate(x.id)} aria-label={`Dismiss ${x.symbol} alert`}>Dismiss</Button>}
      <Button size="sm" onClick={() => setHistoryAlertId(current => current === x.id ? null : x.id)} aria-label={`View ${x.symbol} trigger history`}>Trigger history</Button>
      <IconButton icon="trash" label={`Delete ${x.symbol} alert`} onClick={() => remove(x)} />
      {historyAlertId === x.id ? <PriceAlertHistory alertId={x.id} /> : null}
    </Card></li>)}</ul>}
  </>
}

function PriceAlertHistory({ alertId }: { alertId: string }) {
  const history = usePriceAlertTriggersQuery(alertId)
  if (history.isLoading) return <p className="is-muted">Loading trigger history…</p>
  if (history.isError) return <section aria-label="Trigger history unavailable"><p>Trigger history unavailable.</p><Button size="sm" onClick={() => { void history.refetch() }}>Retry</Button></section>
  const items = history.data?.items ?? []
  if (!items.length) return <p className="is-muted">No trigger history.</p>
  return <section aria-label="Trigger history"><h3>Trigger history</h3><ul className="timeline">{items.map(item => <li key={item.id}>
    <strong>{item.priceType === 'open' ? 'Open' : 'Close'} price {item.observedPrice.toLocaleString()}</strong>
    <span>Trading date {item.tradingDate}</span>
    <span>Triggered {new Date(item.triggeredAt).toLocaleString()}</span>
    <span>{item.dismissedAt ? `Dismissed ${new Date(item.dismissedAt).toLocaleString()}` : 'Active trigger'}</span>
  </li>)}</ul></section>
}

const rotationScopes = ['universe', 'sector'] as const
const rotationMaFilters = ['all', 'above20', 'below20', 'above50', 'below50', 'above200', 'below200'] as const
const rotationSorts = ['rank', 'return2w'] as const
type RotationSort = (typeof rotationSorts)[number]

const rotationNumber = (value: number | string | null) => value == null ? null : Number(value)
const rotationPercent = (value: number | null) => value == null ? '—' : `${value > 0 ? '+' : ''}${value.toFixed(2)}%`
const rotationPercentile = (value: number | null) => value == null ? '—' : `${(value * 100).toLocaleString(undefined, { maximumFractionDigits: 1 })}%`
const rotationLabel = (value: string | null) => value ? value.replaceAll('_', ' ').replace(/\b\w/g, letter => letter.toUpperCase()) : 'Unavailable'
const maLabel = (value: boolean | null) => value == null ? '—' : value ? 'Above' : 'Below'

function sortRotation(items: RotationItem[], sort: RotationSort, direction: 'asc' | 'desc') {
  return [...items].sort((left, right) => {
    const a = sort === 'rank' ? rotationNumber(left.rank2w) : left.return2w
    const b = sort === 'rank' ? rotationNumber(right.rank2w) : right.return2w
    if (a == null) return b == null ? 0 : 1
    if (b == null) return -1
    return (a - b) * (direction === 'asc' ? 1 : -1)
  })
}

function sortPartitionedRotation(items: RotationItem[], sort: RotationSort, direction: 'asc' | 'desc', rankScope: string) {
  if (rankScope !== 'sector') return sortRotation(items, sort, direction)
  const groups = [...new Set(items.map(item => item.rankGroup))].sort()
  return groups.flatMap(group => sortRotation(items.filter(item => item.rankGroup === group), sort, direction))
}

export function RotationPage() {
  const [params, setParams] = useSearchParams()
  const bootstrap = useBootstrapQuery()
  const universesQuery = useRotationUniversesQuery()
  const universes = useMemo(() => universesQuery.data?.items ?? [], [universesQuery.data?.items])
  const requestedScope = params.get('scope') ?? ''
  const validScope = rotationScopes.includes(requestedScope as (typeof rotationScopes)[number]) ? requestedScope : ''
  const requestedUniverse = (params.get('universe') ?? '').toUpperCase()
  const scopedUniverses = validScope ? universes.filter(item => item.rankScope === validScope) : universes
  const selectedUniverse = universes.find(item => item.code === requestedUniverse && (!validScope || item.rankScope === validScope)) ?? scopedUniverses[0] ?? universes[0]
  const scope = selectedUniverse?.rankScope ?? validScope
  const monitor = useRotationQuery(selectedUniverse?.code ?? '', scope)
  const group = params.get('group') ?? 'all'
  const requestedMa = params.get('ma') ?? 'all'
  const ma = rotationMaFilters.includes(requestedMa as (typeof rotationMaFilters)[number]) ? requestedMa : 'all'
  const requestedSort = params.get('sort') ?? 'rank'
  const sort = rotationSorts.includes(requestedSort as RotationSort) ? requestedSort as RotationSort : 'rank'
  const direction = params.get('direction') === 'desc' ? 'desc' : 'asc'
  const groups = useMemo(() => [...new Set((monitor.data?.etfs ?? []).map(item => item.rankGroup))].sort(), [monitor.data?.etfs])

  useEffect(() => {
    if (!selectedUniverse) return
    const next = new URLSearchParams(params)
    let changed = false
    if (next.get('universe') !== selectedUniverse.code) { next.set('universe', selectedUniverse.code); changed = true }
    if (next.get('scope') !== selectedUniverse.rankScope) { next.set('scope', selectedUniverse.rankScope); changed = true }
    if (monitor.isSuccess && next.get('group') && next.get('group') !== 'all' && !groups.includes(next.get('group')!)) { next.delete('group'); changed = true }
    if (next.get('ma') && !rotationMaFilters.includes(next.get('ma') as (typeof rotationMaFilters)[number])) { next.delete('ma'); changed = true }
    if (next.get('sort') && !rotationSorts.includes(next.get('sort') as RotationSort)) { next.delete('sort'); changed = true }
    if (next.get('direction') && !['asc', 'desc'].includes(next.get('direction')!)) { next.delete('direction'); changed = true }
    if (changed) setParams(next, { replace: true })
  }, [groups, monitor.isSuccess, params, selectedUniverse, setParams])

  const setFilter = (name: string, value: string) => {
    const next = new URLSearchParams(params)
    if (!value || value === 'all' || value === 'rank' && name === 'sort' || value === 'asc' && name === 'direction') next.delete(name)
    else next.set(name, value)
    setParams(next)
  }
  const changeUniverse = (code: string) => {
    const universe = universes.find(item => item.code === code)
    const next = new URLSearchParams(params); next.set('universe', code)
    if (universe) next.set('scope', universe.rankScope)
    next.delete('group'); setParams(next)
  }
  const filtered = useMemo(() => (monitor.data?.etfs ?? []).filter(item => {
    if (group !== 'all' && item.rankGroup !== group) return false
    if ((ma === 'above20' && item.aboveMa20 !== true) || (ma === 'below20' && item.aboveMa20 !== false)) return false
    if ((ma === 'above50' && item.aboveMa50 !== true) || (ma === 'below50' && item.aboveMa50 !== false)) return false
    if ((ma === 'above200' && item.aboveMa200 !== true) || (ma === 'below200' && item.aboveMa200 !== false)) return false
    return true
  }), [group, ma, monitor.data?.etfs])
  const rankScope = monitor.data?.universe.rankScope ?? scope
  const rows = useMemo(() => sortPartitionedRotation(filtered, sort, direction, rankScope), [direction, filtered, rankScope, sort])
  const backendRanked = useMemo(() => sortRotation(monitor.data?.etfs ?? [], 'rank', 'asc'), [monitor.data?.etfs])
  const ranked = backendRanked.filter(item => item.rank2w != null)
  const summaryRanked = rankScope === 'sector' && group !== 'all' ? ranked.filter(item => item.rankGroup === group) : ranked
  const leaders = summaryRanked.slice(0, 3).map(item => item.label).join(', ') || 'Unavailable'
  const laggards = summaryRanked.slice(-3).reverse().map(item => item.label).join(', ') || 'Unavailable'
  const snapshotAge = monitor.data?.snapshotDate && bootstrap.data?.currentLocalDate
    ? (Date.parse(`${bootstrap.data.currentLocalDate}T00:00:00Z`) - Date.parse(`${monitor.data.snapshotDate}T00:00:00Z`)) / 86_400_000 : null
  const snapshotAgeLabel = snapshotAge != null && snapshotAge >= 0
    ? `${snapshotAge} calendar ${snapshotAge === 1 ? 'day' : 'days'}` : 'Unavailable'

  if (universesQuery.isLoading) return <PageSkeleton rows={4} />
  if (universesQuery.isError) return <RotationUnavailable retry={() => { void universesQuery.refetch() }} />
  if (!universes.length) return <><PageHeader title="Market rotation" subtitle="Relative group momentum" /><EmptyBox title="No rotation universes configured" hint="A valid universe is required before snapshots can be displayed." /></>
  if (monitor.isLoading && !monitor.data) return <PageSkeleton rows={5} />
  if (monitor.isError) return <RotationUnavailable retry={() => { void monitor.refetch() }} invalid={String(monitor.error).includes('400')} />

  const data = monitor.data
  return <>
    <PageHeader title="Market rotation" subtitle="Existing rotation-v1 results; no frontend market calculations" />
    <div className="rotation-toolbar">
      <div className="rotation-heading-meta">
        <span>Snapshot: {data?.snapshotDate ?? 'Unavailable'}</span>
        <span>Age: {snapshotAgeLabel}</span>
        <span><strong>Universe</strong> {data?.universe.name ?? selectedUniverse?.name}</span>
        <span><strong>Market state</strong> {rotationLabel(data?.marketState.state ?? null)}</span>
      </div>
      <Button size="sm" variant="ghost" loading={monitor.isFetching} onClick={() => { void monitor.refetch() }}>Refresh</Button>
    </div>
    <Card as="section" className="rotation-filters">
      <Field label="Universe"><SelectBox value={selectedUniverse?.code ?? ''} onChange={event => changeUniverse(event.target.value)}>{universes.map(item => <option key={item.id} value={item.code}>{item.name}</option>)}</SelectBox></Field>
      <Field label="Rank scope"><SelectBox value={scope} onChange={event => setFilter('scope', event.target.value)}>{rotationScopes.map(value => <option key={value} value={value}>{rotationLabel(value)}</option>)}</SelectBox></Field>
      <Field label="Group"><SelectBox value={groups.includes(group) ? group : 'all'} onChange={event => setFilter('group', event.target.value)}><option value="all">All groups</option>{groups.map(value => <option key={value}>{value}</option>)}</SelectBox></Field>
      <Field label="MA status"><SelectBox value={ma} onChange={event => setFilter('ma', event.target.value)}><option value="all">All MA statuses</option><option value="above20">Above MA20</option><option value="below20">Below MA20</option><option value="above50">Above MA50</option><option value="below50">Below MA50</option><option value="above200">Above MA200</option><option value="below200">Below MA200</option></SelectBox></Field>
      <Field label="Sort"><SelectBox value={sort} onChange={event => setFilter('sort', event.target.value)}><option value="rank">Rotation rank</option><option value="return2w">2-week performance</option></SelectBox></Field>
      <Field label="Direction"><SelectBox value={direction} onChange={event => setFilter('direction', event.target.value)}><option value="asc">Ascending</option><option value="desc">Descending</option></SelectBox></Field>
    </Card>
    {!data?.snapshotDate ? <EmptyBox title="No rotation snapshot yet" hint="The universe exists, but no calculated snapshot is available." /> : <>
      <div className="rotation-summary">
        <Stat label="Market state" value={rotationLabel(data.marketState.state)} sub={data.marketState.status} />
        <Stat label="Breadth" value={data.marketState.breadthPercent == null ? '—' : `${data.marketState.breadthPercent.toFixed(2)}%`} sub={data.marketState.benchmarkAboveMa200 == null ? 'Benchmark MA200 unavailable' : data.marketState.benchmarkAboveMa200 ? 'Benchmark above MA200' : 'Benchmark below MA200'} />
        {rankScope === 'universe' ? <>
          <Stat label="Leaders" value={leaders} sub="By global backend 2-week rank" />
          <Stat label="Laggards" value={laggards} sub="By global backend 2-week rank" />
        </> : <>
          <Stat label="Rank partition" value="Ranked independently within each sector" sub={`${groups.length} sectors`} />
          <Stat label="Available rows" value={`${data.etfs.length} available rows`} sub={group === 'all' ? 'Select one group for leaders and laggards' : group} />
          {group !== 'all' ? <>
            <Stat label="Leaders" value={leaders} sub={`Within ${group}`} />
            <Stat label="Laggards" value={laggards} sub={`Within ${group}`} />
          </> : null}
        </>}
        <Stat label="Improving groups" value="Unavailable" sub="No backend trend series" />
        <Stat label="Weakening groups" value="Unavailable" sub="No backend trend series" />
      </div>
      <Card as="section" className="rotation-breadth"><h2>Breadth by group</h2><div className="rotation-breadth__grid">{data.sectorBreadth.map(item => <div key={item.sector}><strong>{item.sector}</strong><span>{item.availableCount}/{item.memberCount} available</span><span>MA20 {item.aboveMa20Percent == null ? '—' : `${item.aboveMa20Percent.toFixed(2)}%`}</span><span>MA50 {item.aboveMa50Percent == null ? '—' : `${item.aboveMa50Percent.toFixed(2)}%`}</span><span>MA200 {item.aboveMa200Percent == null ? '—' : `${item.aboveMa200Percent.toFixed(2)}%`}</span></div>)}</div></Card>
      {!data.etfs.length ? <EmptyBox title="Snapshot has no ranked groups" hint="The snapshot is valid but contains no group rows." /> : !rows.length ? <EmptyBox title="No rankings match these filters" hint="Clear a group or MA filter to see the available rows." /> : <RotationTable rows={rows} rankScope={rankScope} />}
    </>}
  </>
}

function RotationUnavailable({ retry, invalid }: { retry: () => void; invalid?: boolean }) {
  return <><PageHeader title="Market rotation" subtitle="Relative group momentum" /><Card className="rotation-unavailable"><h2>{invalid ? 'Invalid rotation request' : 'Rotation data unavailable'}</h2><p>Live rotation data could not be loaded. This is different from an empty universe or snapshot.</p><Button onClick={retry}>Try again</Button></Card></>
}

function RotationTable({ rows, rankScope }: { rows: RotationItem[]; rankScope: string }) {
  return <Card flush as="section" className="rotation-table-card"><div className="rotation-table-scroll"><table className="rotation-table"><thead><tr><th>Rank</th><th>Symbol</th><th>Name / sector</th><th>Last</th><th>2W</th><th>1M</th><th>3M</th><th>2W percentile</th><th>MA status</th><th>Data status</th></tr></thead><tbody>{rows.map(item => <tr key={item.symbol}><td>{item.rank2w == null ? '—' : rankScope === 'sector' ? `#${item.rank2w} in ${item.rankGroup}` : `#${item.rank2w}`}</td><td><strong>{item.symbol}</strong></td><td>{item.label}<small>{item.sector ?? '—'}</small></td><td>{item.close == null ? '—' : item.close.toLocaleString()}</td><td>{rotationPercent(item.return2w)}</td><td>{rotationPercent(item.return1m)}</td><td>{rotationPercent(item.return3m)}</td><td>{rotationPercentile(item.percentile2w)}</td><td><span className="ma-stack">20 {maLabel(item.aboveMa20)} · 50 {maLabel(item.aboveMa50)} · 200 {maLabel(item.aboveMa200)}</span></td><td><Badge tone={item.status === 'ok' ? 'gain' : 'warn'}>{item.status}</Badge></td></tr>)}</tbody></table></div></Card>
}

export function PartnersPage() {
  const q = usePartnersQuery(); const items = q.data?.items ?? []
  const [agentName, setAgentName] = useState(''); const [key, setKey] = useState(''); const [error, setError] = useState('')
  const createAgent = useCreateAgentMutation()
  async function create(e: FormEvent) { e.preventDefault(); setError(''); setKey(''); try { const x = await createAgent.mutateAsync(agentName.trim()); setKey(x.apiKey); setAgentName('') } catch { setError('Could not create the agent.') } }
  async function copyKey() { await navigator.clipboard.writeText(key) }
  return <><PageHeader title="Partners" subtitle="Useful relationships and scoped integrations" />
    <Card className="stack"><h2>Create AI agent</h2><p className="is-muted">The API key is shown once. Copy it now; the cockpit never stores the raw key.</p><form className="inline-form" onSubmit={create}><TextInput aria-label="Agent name" required value={agentName} onChange={e => setAgentName(e.target.value)} placeholder="Agent name" /><Button variant="primary" type="submit" loading={createAgent.isPending}>Create agent</Button></form>{error ? <p className="form-error" role="alert">{error}</p> : null}{key ? <div className="secret-once" role="status"><code>{key}</code><Button size="sm" onClick={copyKey}>Copy key</Button><Button size="sm" variant="ghost" onClick={() => setKey('')}>I saved it</Button></div> : null}</Card>
    <ListState loading={q.isLoading} error={q.isError ? 'error' : undefined} empty={!items.length} retry={() => { void q.refetch() }}><div className="card-grid">{items.map(x => <Card key={x.id} className="stack"><div><strong>{x.partnerType === 'agent' ? 'AI agent' : 'Partner'}</strong> <Badge tone={x.status === 'accepted' ? 'gain' : 'muted'}>{x.status}</Badge></div><p className="is-muted">Partner ID: {x.partnerUserId}</p><span className="article-meta">Linked {new Date(x.createdAt).toLocaleDateString()}</span></Card>)}</div></ListState></>
}

export function ArticlesPage() { const q = useArticlesQuery(); const items = q.data?.items ?? []; return <><PageHeader title="Articles" subtitle="Research worth returning to" /><ListState loading={q.isLoading} error={q.isError ? 'error' : undefined} empty={!items.length} retry={() => { void q.refetch() }}><ul className="article-list">{items.map(x => <li key={x.id}><Link to={`/articles/${x.slug}`}><Card as="article" className="stack"><div className="article-meta">{x.publishedAt ? new Date(x.publishedAt).toLocaleDateString() : x.slug}</div><h2>{x.title}</h2><p className="is-muted">{x.body}</p></Card></Link></li>)}</ul></ListState></> }

export function ArticleDetailPage() {
  const { slug = '' } = useParams()
  const article = useArticleQuery(slug)
  if (article.isLoading) return <PageSkeleton rows={2} />
  if (article.isError || !article.data) return <SectionError onRetry={() => { void article.refetch() }} />
  return <><PageHeader title={article.data.title} subtitle={article.data.publishedAt ? new Date(article.data.publishedAt).toLocaleDateString() : undefined} /><Card as="article"><p className="prose">{article.data.body}</p></Card></>
}

const fields: Record<ToolId, { key: string; label: string; text?: boolean }[]> = {
  'position-sizing': [{ key: 'accountValue', label: 'Account value' }, { key: 'riskPercent', label: 'Risk %' }, { key: 'entryPrice', label: 'Entry price' }, { key: 'stopPrice', label: 'Stop price' }],
  'risk-reward': [{ key: 'entryPrice', label: 'Entry price' }, { key: 'stopPrice', label: 'Stop price' }, { key: 'targetPrice', label: 'Target price' }],
  fire: [{ key: 'annualExpenses', label: 'Annual expenses' }, { key: 'withdrawalRatePercent', label: 'Withdrawal rate %' }, { key: 'investedAssets', label: 'Invested assets' }],
  'relative-value': [{ key: 'assetPrice', label: 'Asset price' }, { key: 'benchmarkPrice', label: 'Benchmark price' }, { key: 'historicalRatio', label: 'Historical ratio' }],
  seasonality: [{ key: 'returns', label: 'Returns (comma-separated)', text: true }],
}

export function ToolsPage() {
  const [searchParams, setSearchParams] = useSearchParams()
  const requested = searchParams.get('tool')
  const initial = requested && isToolId(requested) ? requested : 'position-sizing'
  const [tool, setTool] = useState<ToolId>(initial)
  const [values, setValues] = useState<Record<string, string>>({})
  const [answer, setAnswer] = useState<Record<string, number> | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    if (requested && isToolId(requested) && requested !== tool) {
      setTool(requested)
      setValues({})
      setAnswer(null)
      setError('')
    }
  }, [requested, tool])

  function selectTool(next: ToolId) {
    setTool(next)
    setValues({})
    setAnswer(null)
    setError('')
    setSearchParams(next === 'position-sizing' ? {} : { tool: next }, { replace: true })
  }

  function submit(e: FormEvent) {
    e.preventDefault()
    setError('')
    try {
      const payload = tool === 'seasonality'
        ? { returns: values.returns ?? '' }
        : Object.fromEntries(Object.entries(values).map(([k, v]) => [k, Number(v)]))
      setAnswer(calculateTool(tool, payload))
    } catch {
      setError('Could not calculate this right now.')
    }
  }

  return (
    <>
      <PageHeader title="Tools" subtitle="Fast checks before committing capital" />
      <Card>
        <form className="stack" onSubmit={submit}>
          <Field label="Calculator">
            <SelectBox value={tool} onChange={e => selectTool(e.target.value as ToolId)}>
              <option value="position-sizing">Position size</option>
              <option value="risk-reward">Risk / reward</option>
              <option value="fire">Financial independence</option>
              <option value="relative-value">Relative value</option>
              <option value="seasonality">Seasonality</option>
            </SelectBox>
          </Field>
          <div className="form-row">
            {fields[tool].map(f => (
              <Field key={f.key} label={f.label}>
                <TextInput
                  required
                  type={f.text ? 'text' : 'number'}
                  step={f.text ? undefined : 'any'}
                  value={values[f.key] ?? ''}
                  onChange={e => setValues(v => ({ ...v, [f.key]: e.target.value }))}
                />
              </Field>
            ))}
          </div>
          {error ? <p className="form-error" role="alert">{error}</p> : null}
          <Button variant="primary" type="submit">Calculate</Button>
        </form>
      </Card>
      {answer ? (
        <Card aria-live="polite">
          <h2>Result</h2>
          <div className="stat-row">
            {Object.entries(answer).map(([k, v]) => (
              <Stat key={k} label={k.replace(/([A-Z])/g, ' $1')} value={Number(v).toLocaleString(undefined, { maximumFractionDigits: 4 })} />
            ))}
          </div>
        </Card>
      ) : null}
    </>
  )
}
