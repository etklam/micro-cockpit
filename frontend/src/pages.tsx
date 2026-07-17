import { useEffect, useMemo, useState } from 'react'
import type { FormEvent, ReactNode } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import type { Alert, Discipline } from './features/api'
import { useIdempotencyKey } from './features/api'
import {
  useAlertsQuery, useCalendarQuery, useCreateAlertMutation, useCreateDisciplineMutation,
  useDashboardQuery, useDeleteAlertMutation, useDeleteDisciplineMutation, useDiariesQuery,
  useDismissAlertMutation, useQuickNoteMutation, useSavePerformanceMutation,
  useDisciplinesQuery,
} from './features/queries'
import { Badge, Button, Card, EmptyBox, Field, IconButton, PageHeader, SelectBox, Stat, TextArea, TextInput } from './ui'
import { Icon } from './icons'
import { PageSkeleton, SectionError, useCockpit } from './shell'
import { cx, formatDate, formatLongDate, formatTime, monthLabel, pct, repeatLabel, signed, signedCompact, todayISO } from './format'

export { DiaryDetailPage, DiaryPage } from './screens/diary'

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
export function CalendarPage() {
  const navigate = useNavigate()
  const params = useParams()
  const now = new Date()
  const year = Number(params.year) || now.getFullYear()
  const month = Number(params.month) || now.getMonth() + 1
  const cursor = { year, month }
  const [search, setSearch] = useSearchParams()
  const { data, isLoading: loading, isError: error, refetch: reload } = useCalendarQuery(year, month)
  const requestedDay = search.get('day')
  const selectedFromUrl = validCalendarDay(requestedDay, year, month) ? requestedDay! : todayISO()
  const [selected, setSelected] = useState(selectedFromUrl)
  const [amount, setAmount] = useState('')
  const [capital, setCapital] = useState('')
  const [note, setNote] = useState('')
  const [formError, setFormError] = useState('')
  const savePerformance = useSavePerformanceMutation()

  useEffect(() => { setSelected(selectedFromUrl) }, [selectedFromUrl])

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
      <Link className="text-link cal-review-link" to={`/review/${cursor.year}/${String(cursor.month).padStart(2, '0')}`}>Review this month</Link>

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
                        onClick={() => { setSelected(d.date); const next = new URLSearchParams(search); next.set('day', d.date); setSearch(next) }}
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

function validCalendarDay(value: string | null, year: number, month: number): boolean {
  if (!value || !/^\d{4}-\d{2}-\d{2}$/.test(value)) return false
  const date = new Date(`${value}T00:00:00Z`)
  return !Number.isNaN(date.getTime()) && date.getUTCFullYear() === year && date.getUTCMonth() + 1 === month && date.toISOString().slice(0, 10) === value
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
