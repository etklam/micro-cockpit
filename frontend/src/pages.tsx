import { useEffect, useMemo, useState } from 'react'
import type { FormEvent, ReactNode } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import type { Alert, Diary, Discipline, Transaction } from './features/api'
import { useIdempotencyKey } from './features/api'
import {
  useAlertsQuery, useCalendarQuery, useCreateAlertMutation, useCreateDisciplineMutation,
  useCreateTransactionMutation, useDashboardQuery, useDeleteAlertMutation, useDeleteDiaryMutation,
  useDeleteDisciplineMutation, useDeleteTransactionMutation, useDiariesQuery, useDiaryQuery,
  useDismissAlertMutation, useQuickNoteMutation, useSaveDiaryMutation, useSavePerformanceMutation,
  useDisciplinesQuery,
  useTransactionsQuery,
} from './features/queries'
import { Badge, Button, Card, EmptyBox, Field, IconButton, PageHeader, SelectBox, Stat, TextArea, TextInput } from './ui'
import { Icon } from './icons'
import { PageSkeleton, SectionError, useCockpit } from './App'
import { cx, formatDate, formatLongDate, formatTime, monthLabel, pct, quantity, repeatLabel, signed, signedCompact, todayISO } from './format'

const pnlTone = (n: number | null | undefined): 'gain' | 'loss' | 'muted' =>
  n == null ? 'muted' : n > 0 ? 'gain' : n < 0 ? 'loss' : 'muted'

const PanelLink = ({ children, onClick }: { children: ReactNode; onClick: () => void }) => (
  <Button variant="ghost" size="sm" icon="arrow" onClick={onClick} className="panel__link">{children}</Button>
)

/* =============================== TODAY ============================== */
export function TodayPage() {
  const { go } = useCockpit()
  const { data, isLoading: loading, isError: error, refetch: reload } = useDashboardQuery()
  const [note, setNote] = useState('')
  const [saved, setSaved] = useState(false)
  const idem = useIdempotencyKey()
  const saveQuickNote = useQuickNoteMutation()

  async function saveNote() {
    if (!note.trim() || !data) return
    try {
      await saveQuickNote.mutateAsync({ date: data.localDate, content: note.trim(), key: idem.key() })
      setNote('')
      idem.reset()
      setSaved(true)
      setTimeout(() => setSaved(false), 2500)
    } finally { /* Mutation state drives the button. */ }
  }

  const hour = new Date().getHours()
  const greeting = hour < 5 ? 'Still up?' : hour < 12 ? 'Good morning' : hour < 18 ? 'Good afternoon' : 'Good evening'

  if (loading || !data) {
    return (
      <>
        <PageHeader title={greeting} />
        <PageSkeleton />
      </>
    )
  }

  const perf = data.performance
  const cap = data.capabilities

  return (
    <>
      <PageHeader title={greeting} subtitle={formatLongDate(data.localDate)} />

      {cap?.alerts === 'available' && data.pendingAlerts && data.pendingAlerts > 0 ? (
        <button className="reminder-banner" onClick={() => go('alerts')}>
          <Icon name="bell" size={16} />
          <span>{data.pendingAlerts} reminder{data.pendingAlerts > 1 ? 's' : ''} ready to review</span>
          <Icon name="right" size={16} />
        </button>
      ) : null}

      <Card className="quick-note" as="section">
        <label className="quick-note__label" htmlFor="qn">What did you notice today?</label>
        <TextArea
          id="qn" value={note} onChange={(e) => setNote(e.target.value)}
          placeholder="A signal, a decision, a mistake worth remembering…"
          className="textarea--prose"
        />
        <div className="quick-note__foot">
          <span className={cx('quick-note__status', saved && 'is-ok')}>
            {saved ? <><Icon name="check" size={14} /> Saved</> : 'Captures the moment, dated for today.'}
          </span>
          <Button variant="primary" icon="plus" loading={saveQuickNote.isPending} onClick={saveNote} disabled={!note.trim()}>
            Save note
          </Button>
        </div>
      </Card>

      <div className="card-grid">
        <Card className="panel" as="section">
          <span className="panel__label">Today’s diary</span>
          <div className="panel__body">
            <p className="panel__title">{data.diary.writtenToday ? 'You showed up today.' : 'No entry yet today.'}</p>
            <p className="panel__sub">{data.diary.count} reflection{data.diary.count === 1 ? '' : 's'} in total.</p>
          </div>
          <PanelLink onClick={() => go('diary')}>{data.diary.writtenToday ? 'Keep writing' : 'Open diary'}</PanelLink>
        </Card>

        <Card className="panel" as="section">
          <span className="panel__label">Daily P/L</span>
          <div className="panel__body">
            <span className={cx('pnl-value', 'num', `is-${pnlTone(perf?.pnlAmount)}`)}>
              {perf ? signed(perf.pnlAmount) : '—'}
            </span>
            {perf?.pnlPercent != null ? (
              <span className={cx('pnl-sub', 'num', `is-${pnlTone(perf.pnlAmount)}`)}>{pct(perf.pnlPercent)}</span>
            ) : (
              <span className="pnl-sub is-muted">No result recorded yet.</span>
            )}
          </div>
          <PanelLink onClick={() => go('calendar')}>Open calendar</PanelLink>
        </Card>

        <Card className="panel" as="section">
          <span className="panel__label">Today’s discipline</span>
          <div className="panel__body">
            {data.discipline ? (
              <blockquote className="panel__quote">{data.discipline.content}</blockquote>
            ) : cap?.discipline === 'empty' ? (
              <p className="panel__sub">No principles set yet.</p>
            ) : (
              <p className="panel__sub is-muted">Unavailable right now.</p>
            )}
          </div>
          <PanelLink onClick={() => go('discipline')}>Manage principles</PanelLink>
        </Card>
      </div>

      <section className="recent" aria-labelledby="recent-h">
        <div className="recent__head">
          <h2 id="recent-h">Recent reflections</h2>
          <Button variant="ghost" size="sm" icon="arrow" onClick={() => go('diary')}>All</Button>
        </div>
        {(data.recentDiaries ?? []).length === 0 ? (
          <EmptyBox icon="diary" title="Nothing written yet" hint="Your latest entries will gather here." />
        ) : (
          <ul className="recent__list">
            {(data.recentDiaries ?? []).map((d) => (
              <li key={d.id}>
                <button className="recent__item" onClick={() => go('diary')}>
                  <span className="recent__date">{d.localDate}</span>
                  <span className="recent__title">{d.title}</span>
                  <span className="recent__arrow"><Icon name="right" size={16} /></span>
                </button>
              </li>
            ))}
          </ul>
        )}
      </section>

      {error ? <SectionError onRetry={reload} /> : null}
    </>
  )
}

/* =============================== DIARY ============================== */
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
  return <><PageHeader title={diary.data.title} subtitle={diary.data.localDate} /><Card as="article" className="entry"><p className="prose entry__body">{diary.data.content}</p><div className="form-actions"><Button variant="ghost" onClick={() => navigate('/diary')}>Back to diary</Button><Button variant="danger" loading={removeDiary.isPending} onClick={remove}>Delete entry</Button></div><TransactionPanel diaryId={diaryId} /></Card></>
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
const localDateTimeLocal = () => {
  const d = new Date()
  d.setMinutes(d.getMinutes() - d.getTimezoneOffset())
  return d.toISOString().slice(0, 16)
}

function TransactionPanel({ diaryId }: { diaryId: string }) {
  const { confirm } = useCockpit()
  const { data, isLoading: loading, isError: error, refetch: reload } = useTransactionsQuery(diaryId)
  const items = data?.items ?? []
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
  const deleteTransaction = useDeleteTransactionMutation(diaryId)

  async function add(e: FormEvent) {
    e.preventDefault()
    setFormError('')
    try {
      await createTransaction.mutateAsync({ body: {
        symbol, side, quantity: Number(qty), price: Number(price), currency,
        tradedAt: new Date(tradedAt).toISOString(), notes,
      }, key: idem.key() })
      setSymbol(''); setQty(''); setPrice(''); setNotes('')
      idem.reset()
    } catch {
      setFormError('Could not add the trade.')
    }
  }

  async function remove(t: Transaction) {
    const ok = await confirm({ title: 'Delete trade?', message: `${t.side.toUpperCase()} ${t.symbol} will be removed.`, confirmText: 'Delete', tone: 'danger' })
    if (!ok) return
    await deleteTransaction.mutateAsync(t.id)
  }

  return (
    <div className="trades">
      <form className="trades__form" onSubmit={add}>
        <TextInput aria-label="Symbol" placeholder="Symbol" required value={symbol} onChange={(e) => setSymbol(e.target.value.toUpperCase())} className="input--symbol" />
        <SelectBox aria-label="Side" value={side} onChange={(e) => setSide(e.target.value)}>
          <option value="buy">Buy</option>
          <option value="sell">Sell</option>
        </SelectBox>
        <TextInput aria-label="Quantity" type="number" min="0" step="any" inputMode="decimal" placeholder="Qty" required value={qty} onChange={(e) => setQty(e.target.value)} className="num" />
        <TextInput aria-label="Price" type="number" min="0" step="any" inputMode="decimal" placeholder="Price" required value={price} onChange={(e) => setPrice(e.target.value)} className="num" />
        <TextInput aria-label="Currency" placeholder="CCY" required value={currency} onChange={(e) => setCurrency(e.target.value.toUpperCase())} className="input--ccy" />
        <TextInput aria-label="Traded at" type="datetime-local" required value={tradedAt} onChange={(e) => setTradedAt(e.target.value)} className="input--when" />
        <Button variant="primary" type="submit" icon="plus" loading={createTransaction.isPending} className="trades__add">Add</Button>
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
              <IconButton icon="trash" label="Delete trade" size={15} className="icon-btn--danger trade__del" onClick={() => remove(t)} />
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

/* ============================= CALENDAR ============================= */
export function CalendarPage() {
  const navigate = useNavigate()
  const params = useParams()
  const now = new Date()
  const year = Number(params.year) || now.getFullYear()
  const month = Number(params.month) || now.getMonth() + 1
  const cursor = { year, month }
  const { data, isLoading: loading, isError: error, refetch: reload } = useCalendarQuery(year, month)
  const [selected, setSelected] = useState(todayISO())
  const [amount, setAmount] = useState('')
  const [capital, setCapital] = useState('')
  const [note, setNote] = useState('')
  const [formError, setFormError] = useState('')
  const savePerformance = useSavePerformanceMutation()

  const day = data?.days.find((d) => d.date === selected)
  useEffect(() => {
    setAmount(day?.performance?.pnlAmount != null ? String(day.performance.pnlAmount) : '')
    setNote(day?.performance?.note ?? '')
  }, [selected, day?.performance?.pnlAmount, day?.performance?.note])

  const shift = (delta: number) => {
    const d = new Date(cursor.year, cursor.month - 1 + delta, 1)
    navigate(`/calendar/${d.getFullYear()}/${String(d.getMonth() + 1).padStart(2, '0')}`)
  }

  async function save(e: FormEvent) {
    e.preventDefault()
    setFormError('')
    try {
      await savePerformance.mutateAsync({ date: selected, amount: Number(amount), capital: capital ? Number(capital) : null, note })
    } catch {
      setFormError('Could not save P/L.')
    }
  }

  const firstWeekday = new Date(cursor.year, cursor.month - 1, 1).getDay()
  const summary = data?.summary

  return (
    <>
      <PageHeader
        title="Calendar"
        subtitle={summary ? `${summary.recordedDays} trading day${summary.recordedDays === 1 ? '' : 's'} this month` : undefined}
      />

      {summary ? (
        <div className="stat-row">
          <Stat label="Net P/L" value={signed(summary.total)} tone={pnlTone(summary.total)} />
          <Stat label="Winning days" value={String(summary.profitDays)} tone="gain" />
          <Stat label="Losing days" value={String(summary.lossDays)} tone="loss" />
          <Stat label="Trading days" value={String(summary.recordedDays)} />
        </div>
      ) : null}

      <div className="cal-head">
        <IconButton icon="left" label="Previous month" onClick={() => shift(-1)} />
        <h2 className="cal-head__title">{monthLabel(cursor.year, cursor.month)}</h2>
        <IconButton icon="right" label="Next month" onClick={() => shift(1)} />
      </div>

      {error ? (
        <SectionError onRetry={reload} />
      ) : (
        <Card flush as="section" className="cal">
          <div className="cal__weekdays">
            {['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'].map((w) => <span key={w}>{w}</span>)}
          </div>
          <div className="cal__grid">
            {loading
              ? Array.from({ length: 35 }, (_, i) => <span key={i} className="day day--skel"><span className="skel" style={{ height: '100%' }} /></span>)
              : <>
                  {Array.from({ length: firstWeekday }, (_, i) => <span key={`b${i}`} className="day day--blank" />)}
                  {data?.days.map((d) => {
                    const tone = pnlTone(d.performance?.pnlAmount)
                    const hasNote = d.diaryCount > 0
                    return (
                      <button
                        key={d.date}
                        type="button"
                        className={cx('day', selected === d.date && 'is-selected', tone !== 'muted' && `is-${tone}`)}
                        aria-label={`${formatDate(d.date)}${d.performance ? `, ${signedCompact(d.performance.pnlAmount)}` : ', no result'}${hasNote ? `, ${d.diaryCount} note${d.diaryCount > 1 ? 's' : ''}` : ''}`}
                        onClick={() => setSelected(d.date)}
                      >
                        <span className="day__num num">{Number(d.date.slice(-2))}</span>
                        {d.performance ? (
                          <span className={cx('day__pnl', 'num', `is-${tone}`)}>{signedCompact(d.performance.pnlAmount)}</span>
                        ) : (
                          <span className="day__pnl is-dash">·</span>
                        )}
                        {hasNote ? <span className="day__note" aria-label={`${d.diaryCount} note${d.diaryCount > 1 ? 's' : ''}`} /> : null}
                      </button>
                    )
                  })}
                </>
            }
          </div>
        </Card>
      )}

      <Card as="section" className="pnl-form">
        <div className="pnl-form__head">
          <h2>{selected}</h2>
          {day?.performance ? <Badge tone={pnlTone(day.performance.pnlAmount)}>{pct(day.performance.pnlPercent ?? 0)}</Badge> : null}
        </div>
        <form className="pnl-form__body" onSubmit={save}>
          <div className="form-row">
            <Field label="P/L amount">
              <TextInput type="number" step="any" inputMode="decimal" required value={amount} onChange={(e) => setAmount(e.target.value)} className="num" />
            </Field>
            <Field label="Capital base" hint="Optional" className="field--grow">
              <TextInput type="number" min="0" step="any" inputMode="decimal" value={capital} onChange={(e) => setCapital(e.target.value)} className="num" />
            </Field>
          </div>
          <Field label="Note">
            <TextInput value={note} onChange={(e) => setNote(e.target.value)} placeholder="What shaped the day?" maxLength={280} />
          </Field>
          {formError ? <p className="form-error" role="alert">{formError}</p> : null}
          <div className="form-actions">
            <Button variant="primary" type="submit" icon="check" loading={savePerformance.isPending}>Save P/L</Button>
          </div>
        </form>
      </Card>
    </>
  )
}

/* ============================ DISCIPLINE =========================== */
export function DisciplinePage() {
  const { confirm } = useCockpit()
  const { data, isLoading: loading, isError: error, refetch: reload } = useDisciplinesQuery()
  const items = data?.items ?? []
  const [content, setContent] = useState('')
  const [formError, setFormError] = useState('')
  const createDiscipline = useCreateDisciplineMutation()
  const deleteDiscipline = useDeleteDisciplineMutation()

  async function add(e: FormEvent) {
    e.preventDefault()
    setFormError('')
    try {
      await createDiscipline.mutateAsync(content.trim())
      setContent('')
    } catch {
      setFormError('Could not add the principle.')
    }
  }

  async function remove(d: Discipline) {
    const ok = await confirm({ title: 'Remove principle?', message: 'This will no longer appear as a daily discipline.', confirmText: 'Remove', tone: 'danger' })
    if (!ok) return
    await deleteDiscipline.mutateAsync(d.id)
  }

  return (
    <>
      <PageHeader title="Discipline" subtitle="The rules you keep, returned to you each day." />

      <Card as="section" className="inline-form-wrap">
        <form className="inline-form" onSubmit={add}>
          <TextInput value={content} onChange={(e) => setContent(e.target.value)} placeholder="A principle worth remembering" required maxLength={280} />
          <Button variant="primary" type="submit" icon="plus" loading={createDiscipline.isPending}>Add</Button>
        </form>
        {formError ? <p className="form-error" role="alert">{formError}</p> : null}
      </Card>

      {error ? (
        <SectionError onRetry={reload} />
      ) : loading ? (
        <ul className="principle-list">{Array.from({ length: 3 }, (_, i) => <li key={i}><Card className="principle"><div className="skel" style={{ height: 18, width: '80%' }} /></Card></li>)}</ul>
      ) : items.length === 0 ? (
        <EmptyBox icon="compass" title="No principles yet" hint="Write the rules you want to live by as a trader. One surfaces on your dashboard each day." />
      ) : (
        <ol className="principle-list">
          {items.map((d) => (
            <li key={d.id}>
              <Card as="article" className="principle">
                <blockquote className="principle__text">{d.content}</blockquote>
                <IconButton icon="trash" label="Remove principle" size={16} className="icon-btn--danger" onClick={() => remove(d)} />
              </Card>
            </li>
          ))}
        </ol>
      )}
    </>
  )
}

/* ============================== ALERTS ============================= */
export function AlertsPage() {
  const { confirm } = useCockpit()
  const alertsQuery = useAlertsQuery()
  const diariesQuery = useDiariesQuery()
  const alerts = alertsQuery.data?.items ?? []
  const diaries = useMemo(() => diariesQuery.data?.items ?? [], [diariesQuery.data?.items])
  const loading = alertsQuery.isLoading || diariesQuery.isLoading
  const error = alertsQuery.isError || diariesQuery.isError
  const reload = () => { void alertsQuery.refetch(); void diariesQuery.refetch() }
  const [diaryId, setDiaryId] = useState('')
  const [date, setDate] = useState(todayISO())
  const [time, setTime] = useState('09:00')
  const [repeat, setRepeat] = useState('none')
  const [formError, setFormError] = useState('')
  const timezone = Intl.DateTimeFormat().resolvedOptions().timeZone
  const createAlert = useCreateAlertMutation()
  const dismissAlert = useDismissAlertMutation()
  const deleteAlert = useDeleteAlertMutation()

  useEffect(() => { if (!diaryId && diaries.length) setDiaryId(diaries[0].id) }, [diaryId, diaries])

  async function add(e: FormEvent) {
    e.preventDefault()
    setFormError('')
    try {
      await createAlert.mutateAsync({ diaryId, startLocalDate: date, localTime: time, timezone, repeatMode: repeat })
    } catch {
      setFormError('Could not create the alert.')
    }
  }

  async function dismiss(a: Alert) { await dismissAlert.mutateAsync(a.id) }
  async function remove(a: Alert) {
    const ok = await confirm({ title: 'Delete alert?', confirmText: 'Delete', tone: 'danger' })
    if (!ok) return
    await deleteAlert.mutateAsync(a.id)
  }

  const titleFor = (id: string) => diaries.find((d) => d.id === id)?.title ?? 'Diary reminder'

  return (
    <>
      <PageHeader title="Alerts" subtitle="Reminders to sit down and write." />

      <Card flush as="section" className="alert-form">
        <form className="alert-form__body" onSubmit={add}>
          <div className="form-row">
            <Field label="Diary" className="field--grow">
              <SelectBox required value={diaryId} onChange={(e) => setDiaryId(e.target.value)} disabled={!diaries.length}>
                <option value="" disabled>Choose a diary</option>
                {diaries.map((d) => <option key={d.id} value={d.id}>{d.title}</option>)}
              </SelectBox>
            </Field>
            <Field label="Date">
              <TextInput type="date" required value={date} onChange={(e) => setDate(e.target.value)} />
            </Field>
          </div>
          <div className="form-row">
            <Field label="Time">
              <TextInput type="time" required value={time} onChange={(e) => setTime(e.target.value)} />
            </Field>
            <Field label="Repeat" className="field--grow">
              <SelectBox value={repeat} onChange={(e) => setRepeat(e.target.value)}>
                <option value="none">Once</option>
                <option value="week">Weekdays this week</option>
                <option value="month">Weekdays this month</option>
              </SelectBox>
            </Field>
          </div>
          {formError ? <p className="form-error" role="alert">{formError}</p> : null}
          <div className="form-actions">
            <Button variant="primary" type="submit" icon="bell" loading={createAlert.isPending} disabled={!diaries.length}>
              Create alert
            </Button>
            {!diaries.length && !loading ? <span className="form-hint">Create a diary entry first.</span> : null}
          </div>
        </form>
      </Card>

      {error ? (
        <SectionError onRetry={reload} />
      ) : loading ? (
        <ul className="alert-list">{Array.from({ length: 2 }, (_, i) => <li key={i}><Card className="alert"><div className="skel" style={{ height: 18, width: '60%' }} /></Card></li>)}</ul>
      ) : alerts.length === 0 ? (
        <EmptyBox icon="bell" title="No alerts set" hint="Schedule a nudge to review a specific diary entry." />
      ) : (
        <ul className="alert-list">
          {alerts.map((a) => (
            <li key={a.id}>
              <Card as="article" className="alert">
                <div className="alert__main">
                  <Badge tone={a.status === 'active' ? 'warn' : 'muted'}>{a.status}</Badge>
                  <h3 className="alert__title">{titleFor(a.diaryId)}</h3>
                  <p className="alert__meta">
                    {a.nextLocalDate ?? a.startLocalDate} · {formatTime(a.localTime)} · {repeatLabel(a.repeatMode)}
                  </p>
                </div>
                <div className="alert__actions">
                  {a.status === 'active' ? <Button size="sm" variant="ghost" icon="check" onClick={() => dismiss(a)}>Dismiss</Button> : null}
                  <IconButton icon="trash" label="Delete alert" size={16} className="icon-btn--danger" onClick={() => remove(a)} />
                </div>
              </Card>
            </li>
          ))}
        </ul>
      )}
    </>
  )
}
