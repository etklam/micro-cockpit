import { useEffect, useMemo, useState } from 'react'
import type { FormEvent, ReactNode } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import type { Alert, Discipline, InstrumentDirectoryItem, ObservationSubjectWrite, ObservationUpdate } from './features/api'
import { useIdempotencyKey } from './features/api'
import {
  useAlertsQuery, useBootstrapQuery, useCalendarQuery, useCreateAlertMutation, useCreateDisciplineMutation,
  useDashboardQuery, useDeleteAlertMutation, useDeleteDisciplineMutation, useDiaryPickerQuery, useDiaryQuery,
  useDismissAlertMutation, useInstrumentDirectoryQuery, useQuickObservationMutation, useSavePerformanceMutation,
  useDisciplinesQuery, useTodayObservationQuery, useUpdateObservationMutation,
} from './features/queries'
import { Badge, Button, Card, EmptyBox, Field, IconButton, PageHeader, SelectBox, Stat, TextArea, TextInput } from './ui'
import { Icon } from './icons'
import { PageSkeleton, SectionError, useCockpit } from './shell'
import { cx, formatDate, formatLongDate, formatTime, monthLabel, pct, repeatLabel, signed, signedCompact } from './format'
import { formatTimezoneLabel } from './features/accountTime'
import { useI18n } from './i18n'

export { DiaryDetailPage, DiaryPage } from './screens/diary'

const pnlTone = (n: number | null | undefined): 'gain' | 'loss' | 'muted' =>
  n == null ? 'muted' : n > 0 ? 'gain' : n < 0 ? 'loss' : 'muted'

const PanelLink = ({ children, onClick }: { children: ReactNode; onClick: () => void }) => (
  <Button variant="ghost" size="sm" icon="arrow" onClick={onClick} className="panel__link">{children}</Button>
)

const WEEKDAYS = [
  'calendar.weekday.sun',
  'calendar.weekday.mon',
  'calendar.weekday.tue',
  'calendar.weekday.wed',
  'calendar.weekday.thu',
  'calendar.weekday.fri',
  'calendar.weekday.sat',
] as const

type SubjectDraft = {
  type: '' | 'broad_market' | 'sector' | 'theme' | 'instrument'
  name: string
  instrumentId: string
  market: string
  symbol: string
  displayName: string
}

const emptySubject = (): SubjectDraft => ({ type: '', name: '', instrumentId: '', market: 'US', symbol: '', displayName: '' })
const subjectDraft = (subject: ObservationUpdate['primarySubject']): SubjectDraft => subject ? {
  type: subject.type as SubjectDraft['type'], name: subject.name ?? '', instrumentId: subject.instrumentId ?? '',
  market: subject.market ?? 'US', symbol: subject.symbol ?? '', displayName: subject.displayName ?? '',
} : emptySubject()
const subjectWrite = (subject: SubjectDraft): ObservationSubjectWrite | null => {
  if (!subject.type) return null
  if (subject.type !== 'instrument') return { type: subject.type, name: subject.name }
  return { type: 'instrument', instrumentId: subject.market.trim().toUpperCase() === 'US' ? subject.instrumentId || null : null, market: subject.market, symbol: subject.symbol, displayName: subject.displayName }
}
const subjectLabel = (subject: NonNullable<ObservationUpdate['primarySubject']>) =>
  subject.type === 'instrument' ? `${subject.symbol} · ${subject.displayName}` : subject.name

function SubjectFields({ subject, onChange, instruments, prefix }: { subject: SubjectDraft; onChange: (subject: SubjectDraft) => void; instruments: InstrumentDirectoryItem[]; prefix: string }) {
  const { t } = useI18n()
  const set = (patch: Partial<SubjectDraft>) => onChange({ ...subject, ...patch })
  return <div className="stack">
    <Field label={t(prefix === 'today.observations.primary' ? 'today.observations.primary.type' : 'today.observations.related.type')}>
      <SelectBox value={subject.type} onChange={event => set({ type: event.target.value as SubjectDraft['type'] })}>
        <option value="">{t('today.observations.subject.none')}</option>
        <option value="broad_market">{t('today.observations.subject.broadMarket')}</option>
        <option value="sector">{t('today.observations.subject.sector')}</option>
        <option value="theme">{t('today.observations.subject.theme')}</option>
        <option value="instrument">{t('today.observations.subject.instrument')}</option>
      </SelectBox>
    </Field>
    {subject.type && subject.type !== 'instrument' ? <Field label={t('today.observations.subjectName')}><TextInput value={subject.name} onChange={event => set({ name: event.target.value })} /></Field> : null}
    {subject.type === 'instrument' ? <>
      <Field label={t('today.observations.market')}><TextInput value={subject.market} onChange={event => set({ market: event.target.value, instrumentId: '', symbol: '', displayName: '' })} /></Field>
      {subject.market.trim().toUpperCase() === 'US' ? <Field label={t('today.observations.usInstrument')}>
        <SelectBox value={subject.instrumentId} onChange={event => {
          const selected = instruments.find(item => item.instrumentId === event.target.value)
          set({ instrumentId: event.target.value, symbol: selected?.symbol ?? '', displayName: selected?.name ?? '' })
        }}>
          <option value="">{t('today.observations.chooseInstrument')}</option>
          {instruments.map(item => <option key={item.instrumentId} value={item.instrumentId}>{item.symbol} · {item.name}</option>)}
        </SelectBox>
      </Field> : <>
        <Field label={t('today.observations.symbol')}><TextInput value={subject.symbol} onChange={event => set({ symbol: event.target.value })} /></Field>
        <Field label={t('today.observations.displayName')}><TextInput value={subject.displayName} onChange={event => set({ displayName: event.target.value })} /></Field>
        <p className="form-hint">{t('today.observations.noDailyClose')}</p>
      </>}
    </> : null}
  </div>
}

/* =============================== TODAY ============================== */
export function TodayPage() {
  const { go } = useCockpit()
  const { t, format, locale } = useI18n()
  const { data, isLoading: loading, isError: error, refetch: reload } = useDashboardQuery()
  const observation = useTodayObservationQuery()
  const instruments = useInstrumentDirectoryQuery()
  const [note, setNote] = useState('')
  const [saved, setSaved] = useState(false)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [editContent, setEditContent] = useState('')
  const [editSignal, setEditSignal] = useState('')
  const [editInterpretation, setEditInterpretation] = useState('')
  const [editMentalState, setEditMentalState] = useState('')
  const [editTags, setEditTags] = useState('')
  const [editPrimarySubject, setEditPrimarySubject] = useState<SubjectDraft>(emptySubject)
  const [editRelatedSubjects, setEditRelatedSubjects] = useState<SubjectDraft[]>([])
  const [editEvidenceUrl, setEditEvidenceUrl] = useState('')
  const [editEvidenceTitle, setEditEvidenceTitle] = useState('')
  const [editEvidenceQuote, setEditEvidenceQuote] = useState('')
  const idem = useIdempotencyKey()
  const saveQuickObservation = useQuickObservationMutation()
  const updateObservation = useUpdateObservationMutation()
  const observationTime = useMemo(() => new Intl.DateTimeFormat(locale, {
    hour: '2-digit', minute: '2-digit', hourCycle: 'h23', timeZone: observation.data?.timezone ?? 'UTC',
  }), [locale, observation.data?.timezone])

  async function saveNote() {
    if (!note.trim()) return
    try {
      await saveQuickObservation.mutateAsync({ content: note.trim(), key: idem.key() })
      setNote('')
      idem.reset()
      setSaved(true)
      setTimeout(() => setSaved(false), 2500)
    } finally { /* Mutation state drives the button. */ }
  }

  function beginEdit(update: ObservationUpdate) {
    setEditingId(update.id)
    setEditContent(update.content)
    setEditSignal(update.signal ?? '')
    setEditInterpretation(update.interpretation ?? '')
    setEditMentalState(update.mentalState ?? '')
    setEditTags(update.tags.join(', '))
    setEditPrimarySubject(subjectDraft(update.primarySubject))
    setEditRelatedSubjects(update.relatedSubjects.map(subject => subjectDraft(subject)))
    setEditEvidenceUrl(update.evidence?.url ?? '')
    setEditEvidenceTitle(update.evidence?.title ?? '')
    setEditEvidenceQuote(update.evidence?.quote ?? '')
  }

  function cancelEdit() {
    setEditingId(null)
    setEditContent('')
  }

  async function saveEdit() {
    if (!editingId || !editContent.trim()) return
    const primarySubject = subjectWrite(editPrimarySubject)
    await updateObservation.mutateAsync({ id: editingId, body: {
      content: editContent.trim(), signal: editSignal || null, interpretation: editInterpretation || null,
      mentalState: editMentalState || null, tags: editTags.split(',').map(tag => tag.trim()).filter(Boolean),
      primarySubject, relatedSubjects: editRelatedSubjects.map(subjectWrite).filter((subject): subject is ObservationSubjectWrite => subject !== null),
      evidence: editEvidenceUrl ? { url: editEvidenceUrl, title: editEvidenceTitle || null, quote: editEvidenceQuote || null } : null,
    } })
    cancelEdit()
  }

  const hour = new Date().getHours()
  const greeting = hour < 5
    ? t('today.greeting.late')
    : hour < 12
      ? t('today.greeting.morning')
      : hour < 18
        ? t('today.greeting.afternoon')
        : t('today.greeting.evening')

  const perf = data?.performance
  const cap = data?.capabilities

  return (
    <>
      <PageHeader title={greeting} subtitle={data ? formatLongDate(data.localDate) : undefined} />

      {data && cap?.alerts === 'available' && data.pendingAlerts && data.pendingAlerts > 0 ? (
        <button className="reminder-banner" onClick={() => go('alerts')}>
          <Icon name="bell" size={16} />
          <span>{t('today.reminders', { count: data.pendingAlerts })}</span>
          <Icon name="right" size={16} />
        </button>
      ) : null}

      <Card className="quick-note" as="section">
        <label className="quick-note__label" htmlFor="qn">{t('today.quickNote.label')}</label>
        <TextArea
          id="qn" value={note} onChange={(e) => setNote(e.target.value)}
          placeholder={t('today.quickNote.placeholder')}
          className="textarea--prose"
        />
        <div className="quick-note__foot">
          <span className={cx('quick-note__status', saved && 'is-ok')}>
            {saved ? <><Icon name="check" size={14} /> {t('common.saved')}</> : t('today.quickNote.hint')}
          </span>
          <Button variant="primary" icon="plus" loading={saveQuickObservation.isPending} onClick={saveNote} disabled={!note.trim()}>
            {t('today.quickNote.save')}
          </Button>
        </div>
      </Card>

      {observation.data?.updates.length ? (
        <section className="recent" aria-labelledby="observation-updates-h">
          <div className="recent__head">
            <h2 id="observation-updates-h">{t('today.observations.title')}</h2>
          </div>
          <ol className="timeline">
            {observation.data.updates.map(update => (
              <li key={update.id}>
                {editingId === update.id ? (
                  <div className="stack">
                    <p className="form-hint" role="note">{t('today.observations.honestyReminder')}</p>
                    <Field label={t('today.observations.editLabel')}><TextArea value={editContent} onChange={event => setEditContent(event.target.value)} /></Field>
                    <Field label={t('today.observations.signal')}><TextArea value={editSignal} onChange={event => setEditSignal(event.target.value)} /></Field>
                    <Field label={t('today.observations.interpretation')}><TextArea value={editInterpretation} onChange={event => setEditInterpretation(event.target.value)} /></Field>
                    <Field label={t('today.observations.mentalState')}><TextInput value={editMentalState} onChange={event => setEditMentalState(event.target.value)} /></Field>
                    <Field label={t('today.observations.tags')}><TextInput value={editTags} onChange={event => setEditTags(event.target.value)} /></Field>
                    <SubjectFields subject={editPrimarySubject} onChange={setEditPrimarySubject} instruments={instruments.data ?? []} prefix="today.observations.primary" />
                    {editRelatedSubjects.map((subject, index) => <div className="stack" key={index}>
                      <SubjectFields subject={subject} instruments={instruments.data ?? []} prefix="today.observations.related" onChange={next => setEditRelatedSubjects(current => current.map((item, itemIndex) => itemIndex === index ? next : item))} />
                      <Button variant="ghost" size="sm" onClick={() => setEditRelatedSubjects(current => current.filter((_, itemIndex) => itemIndex !== index))}>{t('today.observations.removeRelated')}</Button>
                    </div>)}
                    <Button variant="ghost" size="sm" onClick={() => setEditRelatedSubjects(current => [...current, emptySubject()])}>{t('today.observations.addRelated')}</Button>
                    <Field label={t('today.observations.evidenceUrl')}><TextInput type="url" value={editEvidenceUrl} onChange={event => setEditEvidenceUrl(event.target.value)} /></Field>
                    <Field label={t('today.observations.evidenceTitle')}><TextInput value={editEvidenceTitle} onChange={event => setEditEvidenceTitle(event.target.value)} /></Field>
                    <Field label={t('today.observations.evidenceQuote')}><TextArea value={editEvidenceQuote} onChange={event => setEditEvidenceQuote(event.target.value)} /></Field>
                    <div className="form-actions">
                      <Button variant="primary" size="sm" loading={updateObservation.isPending} disabled={!editContent.trim()} onClick={saveEdit}>{t('today.observations.saveEdit')}</Button>
                      <Button variant="ghost" size="sm" onClick={cancelEdit}>{t('common.cancel')}</Button>
                    </div>
                  </div>
                ) : (
                  <>
                    <time dateTime={update.recordedAt}>{observationTime.format(new Date(update.recordedAt))}</time>
                    <p>{update.content}</p>
                    {update.signal ? <p><strong>{t('today.observations.signal')}:</strong> {update.signal}</p> : null}
                    {update.interpretation ? <p><strong>{t('today.observations.interpretation')}:</strong> {update.interpretation}</p> : null}
                    {update.mentalState ? <p><strong>{t('today.observations.mentalState')}:</strong> {update.mentalState}</p> : null}
                    {update.primarySubject ? <p><strong>{t('today.observations.primarySubject')}:</strong> {subjectLabel(update.primarySubject)}{update.primarySubject.type === 'instrument' && !update.primarySubject.dailyCloseAvailable ? ` · ${t('today.observations.dailyCloseUnavailable')}` : ''}</p> : null}
                    {update.relatedSubjects.map((subject, index) => <p key={`${subject.type}-${index}`}><strong>{t('today.observations.relatedSubject')}:</strong> {subjectLabel(subject)}{subject.type === 'instrument' && !subject.dailyCloseAvailable ? ` · ${t('today.observations.dailyCloseUnavailable')}` : ''}</p>)}
                    {update.tags.length ? <p><strong>{t('today.observations.tags')}:</strong> {update.tags.join(' · ')}</p> : null}
                    {update.evidence ? <><p><a href={update.evidence.url} target="_blank" rel="noreferrer">{update.evidence.title || update.evidence.url}</a></p>{update.evidence.quote ? <blockquote><strong>{t('today.observations.evidenceQuote')}:</strong> {update.evidence.quote}</blockquote> : null}</> : null}
                    <Button variant="ghost" size="sm" onClick={() => beginEdit(update)}>{t('today.observations.edit')}</Button>
                  </>
                )}
              </li>
            ))}
          </ol>
        </section>
      ) : null}

      {data ? (
        <>
          <div className="card-grid">
            <Card className="panel" as="section">
              <span className="panel__label">{t('today.diary.label')}</span>
              <div className="panel__body">
                <p className="panel__title">{data.diary.writtenToday ? t('today.diary.written') : t('today.diary.empty')}</p>
                <p className="panel__sub">{t('today.diary.count', { count: data.diary.count })}</p>
              </div>
              <PanelLink onClick={() => go('diary')}>{data.diary.writtenToday ? t('today.diary.keepWriting') : t('today.diary.open')}</PanelLink>
            </Card>

            <Card className="panel" as="section">
              <span className="panel__label">{t('today.pnl.label')}</span>
              <div className="panel__body">
                <span className={cx('pnl-value', 'num', `is-${pnlTone(perf?.pnlAmount)}`)}>
                  {perf ? signed(perf.pnlAmount) : format.empty}
                </span>
                {perf?.pnlPercent != null ? (
                  <span className={cx('pnl-sub', 'num', `is-${pnlTone(perf.pnlAmount)}`)}>{pct(perf.pnlPercent)}</span>
                ) : (
                  <span className="pnl-sub is-muted">{t('today.pnl.none')}</span>
                )}
              </div>
              <PanelLink onClick={() => go('calendar')}>{t('today.pnl.openCalendar')}</PanelLink>
            </Card>

            <Card className="panel" as="section">
              <span className="panel__label">{t('today.discipline.label')}</span>
              <div className="panel__body">
                {data.discipline ? (
                  <blockquote className="panel__quote">{data.discipline.content}</blockquote>
                ) : cap?.discipline === 'empty' ? (
                  <p className="panel__sub">{t('today.discipline.empty')}</p>
                ) : (
                  <p className="panel__sub is-muted">{t('common.unavailable')}</p>
                )}
              </div>
              <PanelLink onClick={() => go('discipline')}>{t('today.discipline.manage')}</PanelLink>
            </Card>
          </div>

          <section className="recent" aria-labelledby="recent-h">
            <div className="recent__head">
              <h2 id="recent-h">{t('today.recent.title')}</h2>
              <Button variant="ghost" size="sm" icon="arrow" onClick={() => go('diary')}>{t('today.recent.all')}</Button>
            </div>
            {(data.recentDiaries ?? []).length === 0 ? (
              <EmptyBox icon="diary" title={t('today.recent.emptyTitle')} hint={t('today.recent.emptyHint')} />
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
        </>
      ) : loading ? <PageSkeleton rows={3} /> : null}

      {error || observation.isError ? <SectionError onRetry={() => { void reload(); void observation.refetch() }} /> : null}
    </>
  )
}

/* =============================== CALENDAR ============================== */
export function CalendarPage() {
  const navigate = useNavigate()
  const params = useParams()
  const { t } = useI18n()
  const bootstrap = useBootstrapQuery()
  const accountToday = bootstrap.data?.currentLocalDate
  const year = Number(params.year) || (accountToday ? Number(accountToday.slice(0, 4)) : new Date().getFullYear())
  const month = Number(params.month) || (accountToday ? Number(accountToday.slice(5, 7)) : new Date().getMonth() + 1)
  const cursor = { year, month }
  const [search, setSearch] = useSearchParams()
  const { data, isLoading: loading, isError: error, refetch: reload } = useCalendarQuery(year, month)
  const requestedDay = search.get('day')
  const defaultDay = accountToday && validCalendarDay(accountToday, year, month) ? accountToday : `${year}-${String(month).padStart(2, '0')}-01`
  const selectedFromUrl = validCalendarDay(requestedDay, year, month) ? requestedDay! : defaultDay
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
      setFormError(t('calendar.pnl.saveError'))
    }
  }

  const firstWeekday = new Date(cursor.year, cursor.month - 1, 1).getDay()
  const summary = data?.summary

  return (
    <>
      <PageHeader
        title={t('calendar.title')}
        subtitle={summary ? t('calendar.subtitle', { count: summary.recordedDays }) : undefined}
      />

      {summary ? (
        <div className="stat-row">
          <Stat label={t('calendar.netPnl')} value={signed(summary.total)} tone={pnlTone(summary.total)} />
          <Stat label={t('calendar.winningDays')} value={String(summary.profitDays)} tone="gain" />
          <Stat label={t('calendar.losingDays')} value={String(summary.lossDays)} tone="loss" />
          <Stat label={t('calendar.tradingDays')} value={String(summary.recordedDays)} />
        </div>
      ) : null}

      <div className="cal-head">
        <IconButton icon="left" label={t('calendar.prevMonth')} onClick={() => shift(-1)} />
        <h2 className="cal-head__title">{monthLabel(cursor.year, cursor.month)}</h2>
        <IconButton icon="right" label={t('calendar.nextMonth')} onClick={() => shift(1)} />
      </div>
      <Link className="text-link cal-review-link" to={`/review/${cursor.year}/${String(cursor.month).padStart(2, '0')}`}>{t('calendar.reviewMonth')}</Link>

      {error ? (
        <SectionError onRetry={reload} />
      ) : (
        <Card flush as="section" className="cal">
          <div className="cal__weekdays">
            {WEEKDAYS.map((key) => <span key={key}>{t(key)}</span>)}
          </div>
          <div className="cal__grid">
            {loading
              ? Array.from({ length: 35 }, (_, i) => <span key={i} className="day day--skel"><span className="skel" style={{ height: '100%' }} /></span>)
              : <>
                  {Array.from({ length: firstWeekday }, (_, i) => <span key={`b${i}`} className="day day--blank" />)}
                  {data?.days.map((d) => {
                    const tone = pnlTone(d.performance?.pnlAmount)
                    const hasNote = d.diaryCount > 0
                    const notesLabel = hasNote ? t('calendar.day.notes', { count: d.diaryCount }) : ''
                    return (
                      <button
                        key={d.date}
                        type="button"
                        className={cx('day', selected === d.date && 'is-selected', tone !== 'muted' && `is-${tone}`)}
                        aria-label={`${formatDate(d.date)}${d.performance ? `, ${signedCompact(d.performance.pnlAmount)}` : `, ${t('calendar.day.noResult')}`}${hasNote ? `, ${notesLabel}` : ''}`}
                        onClick={() => { setSelected(d.date); const next = new URLSearchParams(search); next.set('day', d.date); setSearch(next) }}
                      >
                        <span className="day__num num">{Number(d.date.slice(-2))}</span>
                        {d.performance ? (
                          <span className={cx('day__pnl', 'num', `is-${tone}`)}>{signedCompact(d.performance.pnlAmount)}</span>
                        ) : (
                          <span className="day__pnl is-dash">·</span>
                        )}
                        {hasNote ? <span className="day__note" aria-label={notesLabel} /> : null}
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
            <Field label={t('calendar.pnl.amount')}>
              <TextInput type="number" step="any" inputMode="decimal" required value={amount} onChange={(e) => setAmount(e.target.value)} className="num" />
            </Field>
            <Field label={t('calendar.pnl.capital')} hint={t('common.optional')} className="field--grow">
              <TextInput type="number" min="0" step="any" inputMode="decimal" value={capital} onChange={(e) => setCapital(e.target.value)} className="num" />
            </Field>
          </div>
          <Field label={t('calendar.pnl.note')}>
            <TextInput value={note} onChange={(e) => setNote(e.target.value)} placeholder={t('calendar.pnl.notePlaceholder')} maxLength={280} />
          </Field>
          {formError ? <p className="form-error" role="alert">{formError}</p> : null}
          <div className="form-actions">
            <Button variant="primary" type="submit" icon="check" loading={savePerformance.isPending}>{t('calendar.pnl.save')}</Button>
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
  const { t } = useI18n()
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
      setFormError(t('discipline.addError'))
    }
  }

  async function remove(d: Discipline) {
    const ok = await confirm({
      title: t('discipline.removeTitle'),
      message: t('discipline.removeMessage'),
      confirmText: t('discipline.remove'),
      tone: 'danger',
    })
    if (!ok) return
    await deleteDiscipline.mutateAsync(d.id)
  }

  return (
    <>
      <PageHeader title={t('discipline.title')} subtitle={t('discipline.subtitle')} />

      <Card as="section" className="inline-form-wrap">
        <form className="inline-form" onSubmit={add}>
          <TextInput value={content} onChange={(e) => setContent(e.target.value)} placeholder={t('discipline.placeholder')} required maxLength={280} />
          <Button variant="primary" type="submit" icon="plus" loading={createDiscipline.isPending}>{t('discipline.add')}</Button>
        </form>
        {formError ? <p className="form-error" role="alert">{formError}</p> : null}
      </Card>

      {error ? (
        <SectionError onRetry={reload} />
      ) : loading ? (
        <ul className="principle-list">{Array.from({ length: 3 }, (_, i) => <li key={i}><Card className="principle"><div className="skel" style={{ height: 18, width: '80%' }} /></Card></li>)}</ul>
      ) : items.length === 0 ? (
        <EmptyBox icon="compass" title={t('discipline.emptyTitle')} hint={t('discipline.emptyHint')} />
      ) : (
        <ol className="principle-list">
          {items.map((d) => (
            <li key={d.id}>
              <Card as="article" className="principle">
                <blockquote className="principle__text">{d.content}</blockquote>
                <IconButton icon="trash" label={t('discipline.removeLabel')} size={16} className="icon-btn--danger" onClick={() => remove(d)} />
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
  const { t } = useI18n()
  const bootstrap = useBootstrapQuery()
  const alertsQuery = useAlertsQuery()
  const alerts = alertsQuery.data?.items ?? []
  const [pickerQuery, setPickerQuery] = useState('')
  const [debouncedQ, setDebouncedQ] = useState('')
  useEffect(() => {
    const handle = window.setTimeout(() => setDebouncedQ(pickerQuery.trim()), 250)
    return () => window.clearTimeout(handle)
  }, [pickerQuery])
  const diariesQuery = useDiaryPickerQuery(debouncedQ)
  const diaries = useMemo(() => diariesQuery.data?.items ?? [], [diariesQuery.data?.items])
  const loading = alertsQuery.isLoading || bootstrap.isLoading
  const error = alertsQuery.isError || diariesQuery.isError || bootstrap.isError
  const reload = () => { void alertsQuery.refetch(); void diariesQuery.refetch(); void bootstrap.refetch() }
  const [diaryId, setDiaryId] = useState('')
  const [selectedTitle, setSelectedTitle] = useState('')
  const [date, setDate] = useState('')
  const [time, setTime] = useState('09:00')
  const [repeat, setRepeat] = useState('none')
  const [formError, setFormError] = useState('')
  const timezone = bootstrap.data?.timezone ?? ''
  const accountToday = bootstrap.data?.currentLocalDate ?? ''
  const createAlert = useCreateAlertMutation()
  const dismissAlert = useDismissAlertMutation()
  const deleteAlert = useDeleteAlertMutation()
  const selectedDiary = useDiaryQuery(diaryId)

  useEffect(() => {
    if (accountToday && !date) setDate(accountToday)
  }, [accountToday, date])
  useEffect(() => {
    if (!diaryId && diaries.length) {
      setDiaryId(diaries[0].id)
      setSelectedTitle(diaries[0].title)
    }
  }, [diaryId, diaries])
  useEffect(() => {
    if (selectedDiary.data) setSelectedTitle(selectedDiary.data.title)
  }, [selectedDiary.data])

  // Keep titles for alert cards that fall outside the current picker page.
  const titleCache = useMemo(() => {
    const map = new Map<string, string>()
    if (selectedTitle && diaryId) map.set(diaryId, selectedTitle)
    for (const d of diaries) map.set(d.id, d.title)
    return map
  }, [diaries, diaryId, selectedTitle])

  async function add(e: FormEvent) {
    e.preventDefault()
    setFormError('')
    if (!timezone) { setFormError(t('alerts.timezoneLoading')); return }
    if (!diaryId) { setFormError(t('alerts.chooseRequired')); return }
    try {
      await createAlert.mutateAsync({ diaryId, startLocalDate: date, localTime: time, timezone, repeatMode: repeat })
    } catch {
      setFormError(t('alerts.createError'))
    }
  }

  async function dismiss(a: Alert) { await dismissAlert.mutateAsync(a.id) }
  async function remove(a: Alert) {
    const ok = await confirm({ title: t('alerts.deleteTitle'), confirmText: t('common.delete'), tone: 'danger' })
    if (!ok) return
    await deleteAlert.mutateAsync(a.id)
  }

  const titleFor = (id: string) => titleCache.get(id) ?? (selectedDiary.data?.id === id ? selectedDiary.data.title : null) ?? t('alerts.fallbackTitle')

  return (
    <>
      <PageHeader title={t('alerts.title')} subtitle={t('alerts.subtitle')} />

      <Card flush as="section" className="alert-form">
        <form className="alert-form__body" onSubmit={add}>
          <div className="form-row">
            <Field label={t('alerts.findDiary')} className="field--grow" hint={t('alerts.findHint')}>
              <TextInput value={pickerQuery} onChange={(e) => setPickerQuery(e.target.value)} placeholder={t('alerts.searchPlaceholder')} />
            </Field>
          </div>
          <div className="form-row">
            <Field label={t('alerts.diary')} className="field--grow">
              <SelectBox required value={diaryId} onChange={(e) => {
                setDiaryId(e.target.value)
                const match = diaries.find(d => d.id === e.target.value)
                if (match) setSelectedTitle(match.title)
              }} disabled={!diaries.length && !diaryId}>
                {diaryId && !diaries.some(d => d.id === diaryId) ? (
                  <option value={diaryId}>{selectedTitle || t('alerts.selectedDiary')}</option>
                ) : null}
                <option value="" disabled>{t('alerts.chooseDiary')}</option>
                {diaries.map((d) => <option key={d.id} value={d.id}>{d.title} · {d.localDate}</option>)}
              </SelectBox>
            </Field>
            <Field label={t('alerts.date')}>
              <TextInput type="date" required value={date} onChange={(e) => setDate(e.target.value)} />
            </Field>
          </div>
          <div className="form-row">
            <Field label={t('alerts.time')}>
              <TextInput type="time" required value={time} onChange={(e) => setTime(e.target.value)} />
            </Field>
            <Field label={t('alerts.repeat')} className="field--grow">
              <SelectBox value={repeat} onChange={(e) => setRepeat(e.target.value)}>
                <option value="none">{t('alerts.repeat.once')}</option>
                <option value="week">{t('alerts.repeat.week')}</option>
                <option value="month">{t('alerts.repeat.month')}</option>
              </SelectBox>
            </Field>
          </div>
          {timezone ? <p className="form-hint">{t('alerts.timezoneHint', { timezone: formatTimezoneLabel(timezone) })}</p> : null}
          {formError ? <p className="form-error" role="alert">{formError}</p> : null}
          <div className="form-actions">
            <Button variant="primary" type="submit" icon="bell" loading={createAlert.isPending} disabled={!diaryId || !timezone}>
              {t('alerts.create')}
            </Button>
            {!diaries.length && !loading && !diaryId ? <span className="form-hint">{t('alerts.createFirst')}</span> : null}
          </div>
        </form>
      </Card>

      {error ? (
        <SectionError onRetry={reload} />
      ) : loading ? (
        <ul className="alert-list">{Array.from({ length: 2 }, (_, i) => <li key={i}><Card className="alert"><div className="skel" style={{ height: 18, width: '60%' }} /></Card></li>)}</ul>
      ) : alerts.length === 0 ? (
        <EmptyBox icon="bell" title={t('alerts.emptyTitle')} hint={t('alerts.emptyHint')} />
      ) : (
        <ul className="alert-list">
          {alerts.map((a) => (
            <li key={a.id}>
              <Card as="article" className="alert">
                <div className="alert__main">
                  <Badge tone={a.status === 'active' ? 'warn' : 'muted'}>{a.status}</Badge>
                  <h3 className="alert__title"><AlertDiaryTitle diaryId={a.diaryId} fallback={titleFor(a.diaryId)} /></h3>
                  <p className="alert__meta">
                    {a.nextLocalDate ?? a.startLocalDate} · {formatTime(a.localTime)} · {repeatLabel(a.repeatMode)} · {a.timezone}
                  </p>
                </div>
                <div className="alert__actions">
                  {a.status === 'active' ? <Button size="sm" variant="ghost" icon="check" onClick={() => dismiss(a)}>{t('alerts.dismiss')}</Button> : null}
                  <IconButton icon="trash" label={t('alerts.deleteLabel')} size={16} className="icon-btn--danger" onClick={() => remove(a)} />
                </div>
              </Card>
            </li>
          ))}
        </ul>
      )}
    </>
  )
}

function AlertDiaryTitle({ diaryId, fallback }: { diaryId: string; fallback: string }) {
  const diary = useDiaryQuery(diaryId)
  return <>{diary.data?.title ?? fallback}</>
}
