import { useState } from 'react'
import type { FormEvent, ReactNode } from 'react'
import { Link, useParams } from 'react-router-dom'
import type { PriceAlert, WatchlistItem } from './features/api'
import {
  useAddPriceAlertMutation, useAddWatchlistMutation, useArticleQuery, useArticlesQuery,
  useCalculateMutation, useCreateAgentMutation, useDeletePriceAlertMutation, usePartnersQuery,
  usePriceAlertsQuery, useRemoveWatchlistMutation, useResearchNoteQuery, useResearchTimelineQuery,
  useRotationQuery, useSaveResearchNoteMutation, useWatchlistQuery,
} from './features/queries'
import { Badge, Button, Card, EmptyBox, Field, IconButton, PageHeader, SelectBox, Stat, TextArea, TextInput } from './ui'
import { PageSkeleton, SectionError, useCockpit } from './App'
import type { Page } from './App'

const ListState = ({ loading, error, empty, retry, children }: { loading: boolean; error?: string; empty: boolean; retry: () => void; children: ReactNode }) =>
  loading ? <PageSkeleton rows={2} /> : error ? <SectionError onRetry={retry} /> : empty ? <EmptyBox title="Nothing here yet" hint="Add the first item when it becomes useful." /> : <>{children}</>

export function MorePage() {
  const { go } = useCockpit()
  const links: { page: Page; title: string; hint: string }[] = [
    { page: 'alerts', title: 'Diary alerts', hint: 'Return to reflections at the right time.' },
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
  const [symbol, setSymbol] = useState(''); const [price, setPrice] = useState(''); const [direction, setDirection] = useState('above')
  const addPriceAlert = useAddPriceAlertMutation(); const deletePriceAlert = useDeletePriceAlertMutation()
  async function add(e: FormEvent) { e.preventDefault(); await addPriceAlert.mutateAsync({ symbol: symbol.trim().toUpperCase(), threshold: Number(price), condition: direction }); setSymbol(''); setPrice('') }
  async function remove(x: PriceAlert) { await deletePriceAlert.mutateAsync(x.id) }
  const items = result.data?.items ?? []
  return <><PageHeader title="Price alerts" subtitle="Levels worth looking up from the screen" /><Card flush><form className="alert-form__body" onSubmit={add}><div className="form-row"><Field label="Symbol"><TextInput required value={symbol} onChange={e => setSymbol(e.target.value)} /></Field><Field label="Direction"><SelectBox value={direction} onChange={e => setDirection(e.target.value)}><option value="above">Above</option><option value="below">Below</option></SelectBox></Field><Field label="Target price"><TextInput required min="0.0001" step="any" type="number" value={price} onChange={e => setPrice(e.target.value)} /></Field></div><div className="form-actions"><Button variant="primary" type="submit" loading={addPriceAlert.isPending}>Create alert</Button></div></form></Card><ListState loading={result.isLoading} error={result.isError ? 'error' : undefined} empty={!items.length} retry={() => { void result.refetch() }}><ul className="compact-list">{items.map(x => <li key={x.id}><Card><div className="row-main"><strong>{x.symbol}</strong><span>{x.conditionType} {x.threshold.toLocaleString()}</span></div><Badge tone={x.status === 'triggered' ? 'warn' : 'primary'}>{x.status}</Badge><IconButton icon="trash" label={`Delete ${x.symbol} alert`} onClick={() => remove(x)} /></Card></li>)}</ul></ListState></>
}

export function RotationPage() { const q = useRotationQuery(); const items = q.data?.etfs ?? []; return <><PageHeader title="Market rotation" subtitle={q.data?.snapshotDate ? `As of ${q.data.snapshotDate}` : 'Relative momentum, not a recommendation'} /><ListState loading={q.isLoading} error={q.isError ? 'error' : undefined} empty={!items.length} retry={() => { void q.refetch() }}><div className="card-grid">{items.map((x, i) => <Stat key={x.symbol} label={`#${x.rank2w ?? i + 1} · ${x.label}`} value={x.return2w == null ? '—' : `${x.return2w > 0 ? '+' : ''}${x.return2w.toFixed(2)}%`} sub={x.symbol} tone={x.return2w == null ? 'muted' : x.return2w > 0 ? 'gain' : x.return2w < 0 ? 'loss' : 'muted'} />)}</div></ListState></> }

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

type Tool = 'position-sizing' | 'risk-reward' | 'fire' | 'relative-value' | 'seasonality'
const fields: Record<Tool, { key: string; label: string; text?: boolean }[]> = { 'position-sizing': [{ key: 'accountValue', label: 'Account value' }, { key: 'riskPercent', label: 'Risk %' }, { key: 'entryPrice', label: 'Entry price' }, { key: 'stopPrice', label: 'Stop price' }], 'risk-reward': [{ key: 'entryPrice', label: 'Entry price' }, { key: 'stopPrice', label: 'Stop price' }, { key: 'targetPrice', label: 'Target price' }], fire: [{ key: 'annualExpenses', label: 'Annual expenses' }, { key: 'withdrawalRatePercent', label: 'Withdrawal rate %' }, { key: 'investedAssets', label: 'Invested assets' }], 'relative-value': [{ key: 'assetPrice', label: 'Asset price' }, { key: 'benchmarkPrice', label: 'Benchmark price' }, { key: 'historicalRatio', label: 'Historical ratio' }], seasonality: [{ key: 'returns', label: 'Returns (comma-separated)', text: true }] }
export function ToolsPage() {
  const [tool, setTool] = useState<Tool>('position-sizing'); const [values, setValues] = useState<Record<string, string>>({}); const [answer, setAnswer] = useState<Record<string, number> | null>(null); const [error, setError] = useState('')
  const calculate = useCalculateMutation()
  async function submit(e: FormEvent) { e.preventDefault(); setError(''); try { const payload = tool === 'seasonality' ? { returns: (values.returns ?? '').split(',').map(Number) } : Object.fromEntries(Object.entries(values).map(([k, v]) => [k, Number(v)])); setAnswer(await calculate.mutateAsync({ tool, values: payload })) } catch { setError('Could not calculate this right now.') } }
  return <><PageHeader title="Tools" subtitle="Fast checks before committing capital" /><Card><form className="stack" onSubmit={submit}><Field label="Calculator"><SelectBox value={tool} onChange={e => { setTool(e.target.value as Tool); setValues({}); setAnswer(null) }}><option value="position-sizing">Position size</option><option value="risk-reward">Risk / reward</option><option value="fire">Financial independence</option><option value="relative-value">Relative value</option><option value="seasonality">Seasonality</option></SelectBox></Field><div className="form-row">{fields[tool].map(f => <Field key={f.key} label={f.label}><TextInput required type={f.text ? 'text' : 'number'} step={f.text ? undefined : 'any'} value={values[f.key] ?? ''} onChange={e => setValues(v => ({ ...v, [f.key]: e.target.value }))} /></Field>)}</div>{error ? <p className="form-error" role="alert">{error}</p> : null}<Button variant="primary" type="submit" loading={calculate.isPending}>Calculate</Button></form></Card>{answer ? <Card aria-live="polite"><h2>Result</h2><div className="stat-row">{Object.entries(answer).map(([k, v]) => <Stat key={k} label={k.replace(/([A-Z])/g, ' $1')} value={Number(v).toLocaleString(undefined, { maximumFractionDigits: 4 })} />)}</div></Card> : null}</>
}
