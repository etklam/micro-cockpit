import { useEffect, useMemo, useRef, useState } from 'react'
import type { ChangeEvent, FormEvent, KeyboardEvent } from 'react'
import { Link, useLocation, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import type { Diary, DiaryReviewWrite, Transaction } from '../features/api'
import {
  diaryDeleteErrorMessage, diaryMutationErrorMessage, transactionDeleteErrorMessage,
  transactionUpdateErrorMessage, useIdempotencyKey,
} from '../features/api'
import {
  diaryDetailPath, diaryFiltersActive, diaryFiltersToSearch, emptyDiaryFilters, listPathFromReturnTo,
  normalizeTags, parseDiaryFilters, type DiaryFilters,
} from '../features/diaryFilters'
import { accountDateTimeLocalToUtc, formatTimezoneLabel, nowAccountDateTimeLocal, utcToAccountDateTimeLocal } from '../features/accountTime'
import { MarkdownView, plainExcerpt } from '../features/markdown'
import {
  useBootstrapQuery, useCreateTransactionMutation, useDeleteDiaryMutation, useDeleteTransactionMutation,
  useDiariesInfiniteQuery, useDiaryQuery, useSaveDiaryMutation,
  useDeleteDiaryReviewMutation, useDiaryReviewQuery, useDiaryReviewSummaryQuery, useSaveDiaryReviewMutation,
  useTransactionsQuery, useUpdateTransactionMutation,
} from '../features/queries'
import { Badge, Button, Card, EmptyBox, Field, IconButton, PageHeader, SelectBox, Stat, TextArea, TextInput } from '../ui'
import { PageSkeleton, SectionError, useCockpit } from '../shell'
import { quantity } from '../format'

export function DiaryPage() {
  const { confirm } = useCockpit()
  const navigate = useNavigate()
  const [search, setSearch] = useSearchParams()
  const filters = useMemo(() => parseDiaryFilters(search), [search])
  const [keywordDraft, setKeywordDraft] = useState(filters.q)
  const keywordTimer = useRef<number | null>(null)
  useEffect(() => { setKeywordDraft(filters.q) }, [filters.q])
  // Debounce keyword against the latest URL filters so a pending timer cannot clobber newer date/symbol/tag/review.
  useEffect(() => {
    if (keywordTimer.current != null) window.clearTimeout(keywordTimer.current)
    keywordTimer.current = window.setTimeout(() => {
      keywordTimer.current = null
      setSearch(prev => {
        const current = parseDiaryFilters(prev)
        const q = keywordDraft.trim()
        if (q === current.q) return prev
        return diaryFiltersToSearch({ ...current, q })
      }, { replace: true })
    }, 300)
    return () => {
      if (keywordTimer.current != null) {
        window.clearTimeout(keywordTimer.current)
        keywordTimer.current = null
      }
    }
  }, [keywordDraft, setSearch])

  const list = useDiariesInfiniteQuery(filters)
  const items = useMemo(() => {
    const seen = new Set<string>()
    const rows: Diary[] = []
    for (const page of list.data?.pages ?? []) {
      for (const item of page.items) {
        if (seen.has(item.id)) continue
        seen.add(item.id)
        rows.push(item)
      }
    }
    return rows
  }, [list.data])

  const bootstrap = useBootstrapQuery()
  const accountToday = bootstrap.data?.currentLocalDate
  const [title, setTitle] = useState('')
  const [content, setContent] = useState('')
  const [date, setDate] = useState('')
  const [tags, setTags] = useState<string[]>([])
  const [tagDraft, setTagDraft] = useState('')
  const [mode, setMode] = useState<'write' | 'preview'>('write')
  const [editing, setEditing] = useState<Diary | null>(null)
  const [formError, setFormError] = useState('')
  const [listError, setListError] = useState('')
  const idem = useIdempotencyKey()
  const saveDiary = useSaveDiaryMutation()
  const deleteDiary = useDeleteDiaryMutation()
  useEffect(() => {
    if (accountToday && !editing && !date) setDate(accountToday)
  }, [accountToday, editing, date])
  const reviewWindow = useMemo(() => {
    const to = accountToday ?? ''
    if (!to) return { from: '', to: '' }
    const fromDate = new Date(`${to}T00:00:00Z`); fromDate.setUTCDate(fromDate.getUTCDate() - 29)
    return { from: fromDate.toISOString().slice(0, 10), to }
  }, [accountToday])
  const reviewSummary = useDiaryReviewSummaryQuery(reviewWindow.from, reviewWindow.to)
  const activeFilters = diaryFiltersActive(filters)

  function writeFilters(next: DiaryFilters, opts?: { replace?: boolean }) {
    setSearch(diaryFiltersToSearch(next), { replace: opts?.replace ?? false })
  }

  function clearFilters() {
    if (keywordTimer.current != null) {
      window.clearTimeout(keywordTimer.current)
      keywordTimer.current = null
    }
    setKeywordDraft('')
    writeFilters(emptyDiaryFilters, { replace: true })
  }

  function startEdit(d: Diary) {
    setEditing(d); setTitle(d.title); setContent(d.content); setDate(d.localDate)
    setTags([...(d.tags ?? [])]); setTagDraft(''); setMode('write'); setFormError('')
    document.getElementById('diary-form')?.scrollIntoView({ behavior: 'smooth', block: 'start' })
  }
  function reset() {
    setEditing(null); setTitle(''); setContent(''); setDate(accountToday ?? ''); setTags([]); setTagDraft('')
    setMode('write'); setFormError(''); idem.reset()
  }

  function commitTagDraft() {
    const pieces = tagDraft.split(',').map(part => part.trim()).filter(Boolean)
    if (!pieces.length) return
    const next = normalizeTags([...tags, ...pieces])
    if (next.error) { setFormError(next.error); return }
    setTags(next.tags); setTagDraft(''); setFormError('')
  }

  async function submit(e: FormEvent) {
    e.preventDefault()
    setFormError('')
    const normalized = normalizeTags(tagDraft.trim() ? [...tags, ...tagDraft.split(',')] : tags)
    if (normalized.error) { setFormError(normalized.error); return }
    try {
      await saveDiary.mutateAsync({ id: editing?.id, date, title, content, tags: normalized.tags, key: idem.key() })
      reset()
    } catch (error) {
      setFormError(diaryMutationErrorMessage(error))
    }
  }

  async function remove(d: Diary) {
    const ok = await confirm({ title: 'Delete entry?', message: `“${d.title}” and its trades will be removed.`, confirmText: 'Delete', tone: 'danger' })
    if (!ok) return
    setListError('')
    try {
      await deleteDiary.mutateAsync(d.id)
      if (editing?.id === d.id) reset()
    } catch (error) {
      setListError(diaryDeleteErrorMessage(error))
    }
  }

  return (
    <>
      <PageHeader title="Diary" subtitle={`${items.length} loaded reflection${items.length === 1 ? '' : 's'}`} />
      <Link className="text-link" to="/review">Open monthly review</Link>

      <Card as="section" className="review-patterns">
        <h2>Recent review patterns</h2>
        {reviewSummary.isLoading ? <p className="is-muted">Loading review patterns…</p> : reviewSummary.isError ? <SectionError onRetry={() => { void reviewSummary.refetch() }} /> : Number(reviewSummary.data?.reviewedCount ?? 0) === 0 ? (
          <EmptyBox title="No structured reviews yet" hint="Patterns from the last 30 days will appear here." dense />
        ) : <div className="card-grid">
          <Stat label="Reviewed" value={Number(reviewSummary.data?.reviewedCount)} />
          <Stat label="Average discipline" value={reviewSummary.data?.averageDisciplineScore == null ? '—' : Number(reviewSummary.data.averageDisciplineScore).toFixed(1)} />
          <Stat label="Average execution" value={reviewSummary.data?.averageExecutionScore == null ? '—' : Number(reviewSummary.data.averageExecutionScore).toFixed(1)} />
          <Stat label="Top mistakes" value={reviewSummary.data?.topMistakeTags.map(item => item.tag.replaceAll('_', ' ')).join(', ') || '—'} />
        </div>}
      </Card>

      <Card flush as="section" id="diary-form" className="diary-form">
        <form className="diary-form__body" onSubmit={submit}>
          <div className="form-row">
            <Field label="Date">
              <TextInput type="date" required value={date} onChange={(e) => setDate(e.target.value)} />
            </Field>
            <Field label="Title" className="field--grow">
              <TextInput required maxLength={160} value={title} onChange={(e) => setTitle(e.target.value)} placeholder="Name the day" />
            </Field>
          </div>
          <div className="diary-mode" role="tablist" aria-label="Reflection editor mode">
            <Button type="button" size="sm" variant={mode === 'write' ? 'primary' : 'ghost'} aria-selected={mode === 'write'} onClick={() => setMode('write')}>Write</Button>
            <Button type="button" size="sm" variant={mode === 'preview' ? 'primary' : 'ghost'} aria-selected={mode === 'preview'} onClick={() => setMode('preview')}>Preview</Button>
          </div>
          {mode === 'write' ? (
            <Field label="Reflection">
              <TextArea
                required value={content} onChange={(e) => setContent(e.target.value)}
                placeholder="What happened, and what will you remember about it? Markdown is supported."
                className="textarea--prose textarea--lg"
              />
            </Field>
          ) : (
            <Field label="Preview">
              <MarkdownView content={content} className="prose entry__body diary-preview" />
            </Field>
          )}
          <Field label="Tags" hint="Press Enter or comma. Max 10.">
            <div className="tag-editor">
              <div className="tag-chips">
                {tags.map(tag => (
                  <button key={tag} type="button" className="tag-chip" onClick={() => setTags(tags.filter(item => item !== tag))} aria-label={`Remove tag ${tag}`}>
                    {tag} ×
                  </button>
                ))}
              </div>
              <TextInput
                aria-label="Add tag"
                value={tagDraft}
                onChange={(e) => setTagDraft(e.target.value)}
                onKeyDown={(e: KeyboardEvent<HTMLInputElement>) => {
                  if (e.key === 'Enter' || e.key === ',') { e.preventDefault(); commitTagDraft() }
                }}
                onBlur={commitTagDraft}
                placeholder="fomo, breakout"
                disabled={tags.length >= 10}
              />
            </div>
          </Field>
          {formError ? <p className="form-error" role="alert">{formError}</p> : null}
          <div className="form-actions">
            {editing ? <Button variant="ghost" onClick={reset}>Cancel</Button> : null}
            <Button variant="primary" type="submit" icon="check" loading={saveDiary.isPending}>
              {editing ? 'Update entry' : 'Add entry'}
            </Button>
          </div>
        </form>
      </Card>

      <Card as="section" className="diary-filters">
        <div className="form-row diary-filters__row">
          <Field label="Search" className="field--grow">
            <TextInput aria-label="Keyword" value={keywordDraft} onChange={(e) => setKeywordDraft(e.target.value)} placeholder="Title or content" />
          </Field>
          <Field label="From"><TextInput type="date" value={filters.from} onChange={(e) => writeFilters({ ...filters, from: e.target.value })} /></Field>
          <Field label="To"><TextInput type="date" value={filters.to} onChange={(e) => writeFilters({ ...filters, to: e.target.value })} /></Field>
          <Field label="Review">
            <SelectBox value={filters.review} onChange={(e) => writeFilters({ ...filters, review: e.target.value as DiaryFilters['review'] })}>
              <option value="all">All</option>
              <option value="reviewed">Reviewed</option>
              <option value="unreviewed">Unreviewed</option>
            </SelectBox>
          </Field>
          <Field label="Symbol"><TextInput value={filters.symbol} onChange={(e) => writeFilters({ ...filters, symbol: e.target.value.toUpperCase() }, { replace: true })} placeholder="AAPL" /></Field>
          <Field label="Tag"><TextInput value={filters.tag} onChange={(e) => writeFilters({ ...filters, tag: e.target.value.toLowerCase() }, { replace: true })} placeholder="fomo" /></Field>
        </div>
        <div className="diary-filters__summary">
          <span className="is-muted">{items.length} loaded{activeFilters ? ' · filters active' : ''}</span>
          {activeFilters ? <Button size="sm" variant="ghost" onClick={clearFilters}>Clear filters</Button> : null}
        </div>
        {activeFilters ? (
          <div className="tag-chips" aria-label="Active filters">
            {filters.q ? <span className="tag-chip">q: {filters.q}</span> : null}
            {filters.from ? <span className="tag-chip">from: {filters.from}</span> : null}
            {filters.to ? <span className="tag-chip">to: {filters.to}</span> : null}
            {filters.review !== 'all' ? <span className="tag-chip">review: {filters.review}</span> : null}
            {filters.symbol ? <span className="tag-chip">symbol: {filters.symbol}</span> : null}
            {filters.tag ? <span className="tag-chip">tag: {filters.tag}</span> : null}
          </div>
        ) : null}
      </Card>

      {listError ? <p className="form-error" role="alert">{listError}</p> : null}

      {list.isError ? (
        <SectionError onRetry={() => { void list.refetch() }} />
      ) : list.isLoading ? (
        <DiaryListSkeleton />
      ) : items.length === 0 ? (
        <EmptyBox
          icon="diary"
          title={activeFilters ? 'No entries match these filters' : 'Your diary is empty'}
          hint={activeFilters ? 'Try clearing a filter or widening the date range.' : 'Write your first reflection above — name the day, then be honest about it.'}
        />
      ) : (
        <>
          <ul className="entry-list">
            {items.map((d) => (
              <li key={d.id}>
                <Card flush as="article" className="entry">
                  <div className="entry__head">
                    <span className="entry__date">{d.localDate}</span>
                    <div className="entry__actions">
                      <IconButton icon="layers" label="Trades" size={16} onClick={() => navigate(diaryDetailPath(d.id, search.toString()))} />
                      <IconButton icon="edit" label="Edit entry" size={16} onClick={() => startEdit(d)} />
                      <IconButton icon="trash" label="Delete entry" size={16} className="icon-btn--danger" onClick={() => { void remove(d) }} />
                    </div>
                  </div>
                  <h3 className="entry__title">{d.title}</h3>
                  {d.tags?.length ? (
                    <div className="tag-chips entry__tags">
                      {d.tags.map(tag => (
                        <button key={tag} type="button" className="tag-chip" onClick={() => writeFilters({ ...filters, tag })}>{tag}</button>
                      ))}
                    </div>
                  ) : null}
                  {d.content
                    ? <p className="entry__body">{plainExcerpt(d.content)}</p>
                    : <p className="entry__body is-muted">No reflection written.</p>}
                </Card>
              </li>
            ))}
          </ul>
          {list.hasNextPage ? (
            <div className="form-actions">
              <Button variant="subtle" loading={list.isFetchingNextPage} onClick={() => { void list.fetchNextPage() }}>Load more</Button>
            </div>
          ) : null}
        </>
      )}
    </>
  )
}

export function DiaryDetailPage() {
  const { diaryId = '' } = useParams()
  const navigate = useNavigate()
  const [detailSearch] = useSearchParams()
  const listTarget = listPathFromReturnTo(detailSearch.get('returnTo'))
  const backToList = () => navigate(listTarget)
  const { confirm } = useCockpit()
  const diary = useDiaryQuery(diaryId)
  const removeDiary = useDeleteDiaryMutation()
  const [deleteError, setDeleteError] = useState('')
  async function remove() {
    if (!diary.data) return
    const ok = await confirm({ title: 'Delete entry?', message: `“${diary.data.title}” and its trades will be removed.`, confirmText: 'Delete', tone: 'danger' })
    if (!ok) return
    setDeleteError('')
    try {
      await removeDiary.mutateAsync(diaryId)
      navigate(listTarget, { replace: true })
    } catch (error) {
      setDeleteError(diaryDeleteErrorMessage(error))
    }
  }
  if (diary.isLoading) return <PageSkeleton rows={2} />
  if (diary.isError || !diary.data) return <SectionError onRetry={() => { void diary.refetch() }} />
  return <>
    <PageHeader title={diary.data.title} subtitle={diary.data.localDate} />
    <Card as="article" className="entry">
      {diary.data.tags?.length ? <div className="tag-chips entry__tags">{diary.data.tags.map(tag => <span key={tag} className="tag-chip">{tag}</span>)}</div> : null}
      {diary.data.content
        ? <MarkdownView content={diary.data.content} className="prose entry__body" />
        : <p className="entry__body is-muted">No reflection written.</p>}
      {deleteError ? <p className="form-error" role="alert">{deleteError}</p> : null}
      <div className="form-actions">
        <Button variant="ghost" onClick={backToList}>Back to diary</Button>
        <Button variant="danger" loading={removeDiary.isPending} onClick={() => { void remove() }}>Delete entry</Button>
      </div>
      <TransactionPanel diaryId={diaryId} />
    </Card>
    <DecisionReview diaryId={diaryId} />
  </>
}

const reviewTags = ['no_plan', 'fomo', 'poor_timing', 'risk_violation', 'overtrading', 'ignored_signal', 'early_exit', 'late_exit', 'other']
const emptyReview: DiaryReviewWrite = { thesis: null, plannedAction: null, actualAction: null, emotion: null, disciplineScore: null, executionScore: null, processAssessment: null, mistakeTags: [], lesson: null, nextAction: null }

function DecisionReview({ diaryId }: { diaryId: string }) {
  const { confirm } = useCockpit()
  const review = useDiaryReviewQuery(diaryId)
  const save = useSaveDiaryReviewMutation(diaryId)
  const removeReview = useDeleteDiaryReviewMutation(diaryId)
  const [form, setForm] = useState<DiaryReviewWrite>(emptyReview)
  const location = useLocation()
  const detailsRef = useRef<HTMLDetailsElement>(null)
  const headingRef = useRef<HTMLElement>(null)
  const deepLinked = location.hash === '#decision-review'
  useEffect(() => { if (review.data) setForm({ thesis: review.data.thesis, plannedAction: review.data.plannedAction, actualAction: review.data.actualAction, emotion: review.data.emotion, disciplineScore: review.data.disciplineScore, executionScore: review.data.executionScore, processAssessment: review.data.processAssessment, mistakeTags: review.data.mistakeTags ?? [], lesson: review.data.lesson, nextAction: review.data.nextAction }) }, [review.data])
  useEffect(() => {
    if (!deepLinked || review.isLoading) return
    if (detailsRef.current) detailsRef.current.open = true
    document.getElementById('decision-review')?.scrollIntoView?.({ block: 'start' })
    headingRef.current?.focus()
  }, [deepLinked, review.isLoading])
  const text = (key: keyof DiaryReviewWrite) => (event: ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => setForm(current => ({ ...current, [key]: event.target.value || null }))
  const toggleTag = (tag: string) => setForm(current => { const tags = current.mistakeTags ?? []; return { ...current, mistakeTags: tags.includes(tag) ? tags.filter(item => item !== tag) : [...tags, tag] } })
  async function remove() {
    if (!await confirm({ title: 'Delete structured review?', message: 'Your diary and trades will remain.', confirmText: 'Delete', tone: 'danger' })) return
    await removeReview.mutateAsync(); setForm(emptyReview)
  }
  return <Card as="section" className="decision-review" id="decision-review"><details ref={detailsRef}><summary><strong ref={headingRef} tabIndex={-1}>Decision review</strong></summary>
    {review.isLoading ? <p className="is-muted">Loading decision review…</p> : review.isError ? <SectionError onRetry={() => { void review.refetch() }} /> : <form className="diary-form__body" onSubmit={event => { event.preventDefault(); void save.mutateAsync(form) }}>
      {!review.data ? <p className="is-muted">No structured review yet</p> : null}
      <Field label="Thesis"><TextArea value={form.thesis ?? ''} onChange={text('thesis')} /></Field>
      <div className="form-row"><Field label="Planned action" className="field--grow"><TextInput value={form.plannedAction ?? ''} onChange={text('plannedAction')} /></Field><Field label="Actual action" className="field--grow"><TextInput value={form.actualAction ?? ''} onChange={text('actualAction')} /></Field></div>
      <div className="form-row"><Field label="Emotion"><SelectBox value={form.emotion ?? ''} onChange={event => setForm(current => ({ ...current, emotion: event.target.value || null }))}><option value="">Not set</option>{['calm','confident','uncertain','anxious','fomo','frustrated','overconfident','other'].map(value => <option key={value}>{value}</option>)}</SelectBox></Field><Field label="Discipline score"><SelectBox value={form.disciplineScore == null ? '' : String(form.disciplineScore)} onChange={event => setForm(current => ({ ...current, disciplineScore: event.target.value ? Number(event.target.value) : null }))}><option value="">Not set</option>{[1,2,3,4,5].map(value => <option key={value}>{value}</option>)}</SelectBox></Field><Field label="Execution score"><SelectBox value={form.executionScore == null ? '' : String(form.executionScore)} onChange={event => setForm(current => ({ ...current, executionScore: event.target.value ? Number(event.target.value) : null }))}><option value="">Not set</option>{[1,2,3,4,5].map(value => <option key={value}>{value}</option>)}</SelectBox></Field></div>
      <Field label="Process assessment"><SelectBox value={form.processAssessment ?? ''} onChange={event => setForm(current => ({ ...current, processAssessment: event.target.value || null }))}><option value="">Not set</option><option value="good">Good</option><option value="mixed">Mixed</option><option value="poor">Poor</option></SelectBox></Field>
      <fieldset className="review-tags"><legend>Mistake tags</legend>{reviewTags.map(tag => <label key={tag}><input type="checkbox" checked={(form.mistakeTags ?? []).includes(tag)} onChange={() => toggleTag(tag)} /> {tag.replaceAll('_', ' ')}</label>)}</fieldset>
      <div className="form-row"><Field label="Lesson" className="field--grow"><TextArea value={form.lesson ?? ''} onChange={text('lesson')} /></Field><Field label="Next action" className="field--grow"><TextArea value={form.nextAction ?? ''} onChange={text('nextAction')} /></Field></div>
      <div className="form-actions">{review.data ? <Button variant="danger" loading={removeReview.isPending} onClick={remove}>Delete review</Button> : null}<Button variant="primary" type="submit" loading={save.isPending}>Save review</Button></div>
    </form>}
  </details></Card>
}

function DiaryListSkeleton() {
  return (
    <ul className="entry-list">
      {Array.from({ length: 3 }, (_, i) => (
        <li key={i}><Card flush className="entry"><div className="entry__skel">
          <div className="skel" style={{ height: 12, width: 90 }} />
          <div className="skel" style={{ height: 22, width: '55%', marginTop: 14 }} />
          <div className="skel" style={{ height: 14, width: '90%', marginTop: 12 }} />
          <div className="skel" style={{ height: 14, width: '70%', marginTop: 8 }} />
        </div></Card></li>
      ))}
    </ul>
  )
}

/* -------------------------- Transactions --------------------------- */
function TransactionPanel({ diaryId }: { diaryId: string }) {
  const { confirm } = useCockpit()
  const bootstrap = useBootstrapQuery()
  const timeZone = bootstrap.data?.timezone ?? ''
  const baseCurrency = bootstrap.data?.baseCurrency ?? 'USD'
  const { data, isLoading: loading, isError: error, refetch: reload } = useTransactionsQuery(diaryId)
  const items = data?.items ?? []
  const [editingId, setEditingId] = useState<string | null>(null)
  const [symbol, setSymbol] = useState('')
  const [side, setSide] = useState('buy')
  const [qty, setQty] = useState('')
  const [price, setPrice] = useState('')
  const [currency, setCurrency] = useState(baseCurrency)
  const [tradedAt, setTradedAt] = useState('')
  const [notes, setNotes] = useState('')
  const [formError, setFormError] = useState('')
  const idem = useIdempotencyKey()
  const createTransaction = useCreateTransactionMutation(diaryId)
  const updateTransaction = useUpdateTransactionMutation(diaryId)
  const deleteTransaction = useDeleteTransactionMutation(diaryId)
  const saving = createTransaction.isPending || updateTransaction.isPending

  useEffect(() => {
    if (!timeZone || editingId) return
    if (!tradedAt) setTradedAt(nowAccountDateTimeLocal(timeZone))
    if (!editingId) setCurrency(current => current || baseCurrency)
  }, [timeZone, baseCurrency, editingId, tradedAt])

  function resetEdit() {
    setEditingId(null); setSymbol(''); setSide('buy'); setQty(''); setPrice(''); setCurrency(baseCurrency)
    setTradedAt(timeZone ? nowAccountDateTimeLocal(timeZone) : ''); setNotes(''); setFormError(''); idem.reset()
  }

  function startEdit(t: Transaction) {
    setEditingId(t.id); setSymbol(t.symbol); setSide(t.side); setQty(String(t.quantity)); setPrice(String(t.price))
    setCurrency(t.currency)
    setTradedAt(timeZone ? utcToAccountDateTimeLocal(t.tradedAt, timeZone) : '')
    setNotes(t.notes ?? ''); setFormError(''); idem.reset()
  }

  async function submit(e: FormEvent) {
    e.preventDefault()
    if (saving) return
    setFormError('')

    const normalizedSymbol = symbol.trim().toUpperCase()
    const normalizedCurrency = currency.trim().toUpperCase()
    const quantityValue = Number(qty)
    const priceValue = Number(price)
    setSymbol(normalizedSymbol)
    setCurrency(normalizedCurrency)

    if (!normalizedSymbol) { setFormError('Enter a symbol.'); return }
    if (side !== 'buy' && side !== 'sell') { setFormError('Choose Buy or Sell.'); return }
    if (!Number.isFinite(quantityValue) || !Number.isFinite(priceValue) || quantityValue <= 0 || priceValue <= 0) {
      setFormError('Quantity and price must be greater than zero.'); return
    }
    if (!/^[A-Z]{3}$/.test(normalizedCurrency)) { setFormError('Enter a three-letter currency code.'); return }
    if (!timeZone) { setFormError('Account timezone is still loading.'); return }
    const converted = accountDateTimeLocalToUtc(tradedAt, timeZone)
    if (!converted.ok) {
      setFormError(converted.error === 'nonexistent'
        ? 'That local time does not exist (DST gap). Pick another minute.'
        : 'Enter a valid traded date and time in the account timezone.')
      return
    }

    const body = { symbol: normalizedSymbol, side, quantity: quantityValue, price: priceValue, currency: normalizedCurrency, tradedAt: converted.iso, notes }
    try {
      if (editingId) {
        await updateTransaction.mutateAsync({ id: editingId, body })
        resetEdit()
      } else {
        await createTransaction.mutateAsync({ body, key: idem.key() })
        setSymbol(''); setQty(''); setPrice(''); setNotes(''); setTradedAt(nowAccountDateTimeLocal(timeZone)); idem.reset()
      }
    } catch (mutationError: unknown) {
      setFormError(editingId ? transactionUpdateErrorMessage(mutationError) : 'Could not add the trade.')
    }
  }

  async function remove(t: Transaction) {
    const ok = await confirm({ title: 'Delete trade?', message: `${t.side.toUpperCase()} ${t.symbol} will be removed.`, confirmText: 'Delete', tone: 'danger' })
    if (!ok) return
    try {
      await deleteTransaction.mutateAsync(t.id)
      if (editingId === t.id) resetEdit()
    } catch (mutationError: unknown) {
      setFormError(transactionDeleteErrorMessage(mutationError))
    }
  }

  if (!timeZone) return <p className="is-muted">Loading account timezone…</p>

  return (
    <div className="trades">
      <form className="trades__form" onSubmit={submit}>
        <TextInput aria-label="Symbol" placeholder="Symbol" required disabled={saving} value={symbol} onChange={(e) => setSymbol(e.target.value.toUpperCase())} className="input--symbol" />
        <SelectBox aria-label="Side" disabled={saving} value={side} onChange={(e) => setSide(e.target.value)}>
          <option value="buy">Buy</option>
          <option value="sell">Sell</option>
        </SelectBox>
        <TextInput aria-label="Quantity" type="number" min="0" step="any" inputMode="decimal" placeholder="Qty" required disabled={saving} value={qty} onChange={(e) => setQty(e.target.value)} className="num" />
        <TextInput aria-label="Price" type="number" min="0" step="any" inputMode="decimal" placeholder="Price" required disabled={saving} value={price} onChange={(e) => setPrice(e.target.value)} className="num" />
        <TextInput aria-label="Currency" placeholder="CCY" required maxLength={3} disabled={saving} value={currency} onChange={(e) => setCurrency(e.target.value.toUpperCase())} className="input--ccy" />
        <div className="trades__when">
          <TextInput aria-label="Traded at" type="datetime-local" required disabled={saving} value={tradedAt} onChange={(e) => setTradedAt(e.target.value)} className="input--when" />
          <span className="form-hint" title={timeZone}>{formatTimezoneLabel(timeZone)}</span>
        </div>
        <TextInput aria-label="Notes" placeholder="Notes (optional)" disabled={saving} value={notes} onChange={(e) => setNotes(e.target.value)} />
        {editingId ? <Button variant="ghost" disabled={saving} onClick={resetEdit}>Cancel</Button> : null}
        <Button variant="primary" type="submit" icon={editingId ? 'check' : 'plus'} loading={saving} className="trades__add">{editingId ? 'Save changes' : 'Add'}</Button>
      </form>
      {formError ? <p className="form-error" role="alert">{formError}</p> : null}

      {error ? (
        <SectionError onRetry={reload} />
      ) : loading ? (
        <div className="trades__rows">{Array.from({ length: 2 }, (_, i) => <div key={i} className="skel" style={{ height: 22 }} />)}</div>
      ) : items.length === 0 ? (
        <p className="trades__empty">No trades logged.</p>
      ) : (
        <ul className="trades__rows">
          {items.map((t) => (
            <li key={t.id} className="trade">
              <Badge tone={t.side === 'buy' ? 'gain' : 'loss'}>{t.side === 'buy' ? 'Buy' : 'Sell'}</Badge>
              <span className="trade__sym">{t.symbol}</span>
              <span className="trade__qty num">{quantity(t.quantity)} × {t.price} {t.currency}</span>
              {t.notes ? <span className="trade__notes">{t.notes}</span> : null}
              <div className="trade__actions">
                <IconButton icon="edit" label={`Edit ${t.symbol} trade`} size={15} aria-pressed={editingId === t.id} disabled={saving || deleteTransaction.isPending} onClick={() => startEdit(t)} />
                <IconButton icon="trash" label="Delete trade" size={15} className="icon-btn--danger" disabled={saving || deleteTransaction.isPending} onClick={() => remove(t)} />
              </div>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

/* ============================= CALENDAR ============================= */
