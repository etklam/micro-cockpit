import { useEffect, useMemo, useRef, useState } from 'react'
import type { ChangeEvent, FormEvent } from 'react'
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom'
import type { Diary, DiaryReviewWrite, Transaction } from '../features/api'
import { transactionUpdateErrorMessage, useIdempotencyKey } from '../features/api'
import {
  useBootstrapQuery, useCreateTransactionMutation, useDeleteDiaryMutation, useDeleteTransactionMutation,
  useDiariesQuery, useDiaryQuery, useSaveDiaryMutation,
  useDeleteDiaryReviewMutation, useDiaryReviewQuery, useDiaryReviewSummaryQuery, useSaveDiaryReviewMutation,
  useTransactionsQuery, useUpdateTransactionMutation,
} from '../features/queries'
import { Badge, Button, Card, EmptyBox, Field, IconButton, PageHeader, SelectBox, Stat, TextArea, TextInput } from '../ui'
import { PageSkeleton, SectionError, useCockpit } from '../shell'
import { quantity, todayISO } from '../format'

export function DiaryPage() {
  const { confirm } = useCockpit()
  const navigate = useNavigate()
  const { data, isLoading: loading, isError: error, refetch: reload } = useDiariesQuery()
  const items = data?.items ?? []
  const [title, setTitle] = useState('')
  const [content, setContent] = useState('')
  const [date, setDate] = useState(todayISO())
  const [editing, setEditing] = useState<Diary | null>(null)
  const [formError, setFormError] = useState('')
  const idem = useIdempotencyKey()
  const saveDiary = useSaveDiaryMutation()
  const deleteDiary = useDeleteDiaryMutation()
  const bootstrap = useBootstrapQuery()
  const reviewWindow = useMemo(() => {
    const to = bootstrap.data?.currentLocalDate ?? ''
    if (!to) return { from: '', to: '' }
    const fromDate = new Date(`${to}T00:00:00Z`); fromDate.setUTCDate(fromDate.getUTCDate() - 29)
    return { from: fromDate.toISOString().slice(0, 10), to }
  }, [bootstrap.data?.currentLocalDate])
  const reviewSummary = useDiaryReviewSummaryQuery(reviewWindow.from, reviewWindow.to)

  function startEdit(d: Diary) {
    setEditing(d); setTitle(d.title); setContent(d.content); setDate(d.localDate)
    document.getElementById('diary-form')?.scrollIntoView({ behavior: 'smooth', block: 'start' })
  }
  function reset() { setEditing(null); setTitle(''); setContent(''); setDate(todayISO()); setFormError(''); idem.reset() }

  async function submit(e: FormEvent) {
    e.preventDefault()
    setFormError('')
    try {
      await saveDiary.mutateAsync({ id: editing?.id, date, title, content, key: idem.key() })
      reset()
    } catch {
      setFormError('Could not save the entry.')
    }
  }

  async function remove(d: Diary) {
    const ok = await confirm({ title: 'Delete entry?', message: `“${d.title}” and its trades will be removed.`, confirmText: 'Delete', tone: 'danger' })
    if (!ok) return
    await deleteDiary.mutateAsync(d.id)
    if (editing?.id === d.id) reset()
  }

  return (
    <>
      <PageHeader title="Diary" subtitle={`${items.length} reflection${items.length === 1 ? '' : 's'}`} />
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
          <Field label="Reflection">
            <TextArea
              required value={content} onChange={(e) => setContent(e.target.value)}
              placeholder="What happened, and what will you remember about it?"
              className="textarea--prose textarea--lg"
            />
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

      {error ? (
        <SectionError onRetry={reload} />
      ) : loading ? (
        <DiaryListSkeleton />
      ) : items.length === 0 ? (
        <EmptyBox icon="diary" title="Your diary is empty" hint="Write your first reflection above — name the day, then be honest about it." />
      ) : (
        <ul className="entry-list">
          {items.map((d) => (
            <li key={d.id}>
              <Card flush as="article" className="entry">
                <div className="entry__head">
                  <span className="entry__date">{d.localDate}</span>
                  <div className="entry__actions">
                    <IconButton icon="layers" label="Trades" size={16} onClick={() => navigate(`/diary/${d.id}`)} />
                    <IconButton icon="edit" label="Edit entry" size={16} onClick={() => startEdit(d)} />
                    <IconButton icon="trash" label="Delete entry" size={16} className="icon-btn--danger" onClick={() => remove(d)} />
                  </div>
                </div>
                <h3 className="entry__title">{d.title}</h3>
                {d.content ? <p className="prose entry__body">{d.content}</p> : <p className="entry__body is-muted">No reflection written.</p>}
              </Card>
            </li>
          ))}
        </ul>
      )}
    </>
  )
}

export function DiaryDetailPage() {
  const { diaryId = '' } = useParams()
  const navigate = useNavigate()
  const { confirm } = useCockpit()
  const diary = useDiaryQuery(diaryId)
  const removeDiary = useDeleteDiaryMutation()
  async function remove() {
    if (!diary.data) return
    const ok = await confirm({ title: 'Delete entry?', message: `“${diary.data.title}” and its trades will be removed.`, confirmText: 'Delete', tone: 'danger' })
    if (!ok) return
    await removeDiary.mutateAsync(diaryId)
    navigate('/diary', { replace: true })
  }
  if (diary.isLoading) return <PageSkeleton rows={2} />
  if (diary.isError || !diary.data) return <SectionError onRetry={() => { void diary.refetch() }} />
  return <><PageHeader title={diary.data.title} subtitle={diary.data.localDate} /><Card as="article" className="entry"><p className="prose entry__body">{diary.data.content}</p><div className="form-actions"><Button variant="ghost" onClick={() => navigate('/diary')}>Back to diary</Button><Button variant="danger" loading={removeDiary.isPending} onClick={remove}>Delete entry</Button></div><TransactionPanel diaryId={diaryId} /></Card><DecisionReview diaryId={diaryId} /></>
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
const localDateTimeLocal = (value: string | Date = new Date()) => {
  const d = value instanceof Date ? new Date(value.getTime()) : new Date(value)
  if (Number.isNaN(d.getTime())) return ''
  d.setMinutes(d.getMinutes() - d.getTimezoneOffset())
  return d.toISOString().slice(0, 16)
}

function TransactionPanel({ diaryId }: { diaryId: string }) {
  const { confirm } = useCockpit()
  const { data, isLoading: loading, isError: error, refetch: reload } = useTransactionsQuery(diaryId)
  const items = data?.items ?? []
  const [editingId, setEditingId] = useState<string | null>(null)
  const [symbol, setSymbol] = useState('')
  const [side, setSide] = useState('buy')
  const [qty, setQty] = useState('')
  const [price, setPrice] = useState('')
  const [currency, setCurrency] = useState('USD')
  const [tradedAt, setTradedAt] = useState(localDateTimeLocal)
  const [notes, setNotes] = useState('')
  const [formError, setFormError] = useState('')
  const idem = useIdempotencyKey()
  const createTransaction = useCreateTransactionMutation(diaryId)
  const updateTransaction = useUpdateTransactionMutation(diaryId)
  const deleteTransaction = useDeleteTransactionMutation(diaryId)
  const saving = createTransaction.isPending || updateTransaction.isPending

  function resetEdit() {
    setEditingId(null); setSymbol(''); setSide('buy'); setQty(''); setPrice(''); setCurrency('USD')
    setTradedAt(localDateTimeLocal()); setNotes(''); setFormError(''); idem.reset()
  }

  function startEdit(t: Transaction) {
    setEditingId(t.id); setSymbol(t.symbol); setSide(t.side); setQty(String(t.quantity)); setPrice(String(t.price))
    setCurrency(t.currency); setTradedAt(localDateTimeLocal(t.tradedAt)); setNotes(t.notes ?? ''); setFormError(''); idem.reset()
  }

  async function submit(e: FormEvent) {
    e.preventDefault()
    if (saving) return
    setFormError('')

    const normalizedSymbol = symbol.trim().toUpperCase()
    const normalizedCurrency = currency.trim().toUpperCase()
    const quantityValue = Number(qty)
    const priceValue = Number(price)
    const tradeTime = new Date(tradedAt)
    setSymbol(normalizedSymbol)
    setCurrency(normalizedCurrency)

    if (!normalizedSymbol) { setFormError('Enter a symbol.'); return }
    if (side !== 'buy' && side !== 'sell') { setFormError('Choose Buy or Sell.'); return }
    if (!Number.isFinite(quantityValue) || !Number.isFinite(priceValue) || quantityValue <= 0 || priceValue <= 0) {
      setFormError('Quantity and price must be greater than zero.'); return
    }
    if (!/^[A-Z]{3}$/.test(normalizedCurrency)) { setFormError('Enter a three-letter currency code.'); return }
    if (!tradedAt || Number.isNaN(tradeTime.getTime())) { setFormError('Enter a valid traded date and time.'); return }

    const body = { symbol: normalizedSymbol, side, quantity: quantityValue, price: priceValue, currency: normalizedCurrency, tradedAt: tradeTime.toISOString(), notes }
    try {
      if (editingId) {
        await updateTransaction.mutateAsync({ id: editingId, body })
        resetEdit()
      } else {
        await createTransaction.mutateAsync({ body, key: idem.key() })
        setSymbol(''); setQty(''); setPrice(''); setNotes(''); idem.reset()
      }
    } catch (mutationError: unknown) {
      setFormError(editingId ? transactionUpdateErrorMessage(mutationError) : 'Could not add the trade.')
    }
  }

  async function remove(t: Transaction) {
    const ok = await confirm({ title: 'Delete trade?', message: `${t.side.toUpperCase()} ${t.symbol} will be removed.`, confirmText: 'Delete', tone: 'danger' })
    if (!ok) return
    await deleteTransaction.mutateAsync(t.id)
    if (editingId === t.id) resetEdit()
  }

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
        <TextInput aria-label="Traded at" type="datetime-local" required disabled={saving} value={tradedAt} onChange={(e) => setTradedAt(e.target.value)} className="input--when" />
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
