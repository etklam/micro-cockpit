import { useEffect, useMemo, useState } from 'react'
import type { FormEvent, ReactNode } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import type { ActionDecision, ActionDecisionIntent, ComparisonQuery, Discipline, DisciplinePrincipleStatus, ExecutionReview, Expectation, ExpectationConfidence, ExpectationDeadlinePreset, ExpectationReviewWrite, InstrumentDirectoryItem, ObservationSearchFilters, ObservationSubjectWrite, ObservationUpdate, OwnerComparison, ReasoningLabelKind } from './features/api'
import { useIdempotencyKey } from './features/api'
import {
  useActionDecisionsQuery, useAgentsQuery, useBootstrapQuery, useCalendarQuery, useComparisonQuery, useCreateActionDecisionMutation, useCreateDisciplineMutation,
  useCreateTradeEvidenceMutation, useDeleteActionDecisionMutation,
  useExpectationsQuery, useInstrumentDirectoryQuery, useInvalidateExpectationMutation, useObservationHistoryQuery, useQuickObservationMutation,
  useCreateExpectationMutation, useCreateReasoningLabelMutation, useDisciplinesQuery, useExpectationReviewQuery, useReasoningLabelsQuery,
  usePatternReviewQuery, useSaveExpectationReviewMutation, useSelectDisciplineMutation, useTodayDisciplineQuery, useTodayObservationQuery, useTradeEvidenceQuery,
  useUpdateActionDecisionMutation, useUpdateDisciplineMutation, useUpdateExpectationMutation, useUpdateObservationMutation,
} from './features/queries'
import { Badge, Button, Card, EmptyBox, Field, IconButton, PageHeader, SelectBox, TextArea, TextInput } from './ui'
import { Icon } from './icons'
import { PageSkeleton, SectionError, useCockpit } from './shell'
import { cx, formatDate, formatLongDate, monthLabel } from './format'
import { accountDateTimeLocalToUtc, formatTimezoneLabel, utcToAccountDateTimeLocal } from './features/accountTime'
import { useI18n } from './i18n'

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
const subjectHistoryHref = (subject: NonNullable<ObservationUpdate['primarySubject']>) => {
  const params = new URLSearchParams()
  if (subject.type === 'instrument') {
    if (subject.instrumentId) params.set('instrumentId', subject.instrumentId)
    else { if (subject.market) params.set('market', subject.market); if (subject.symbol) params.set('symbol', subject.symbol) }
  } else { params.set('subjectType', subject.type); if (subject.name) params.set('subject', subject.name) }
  return `/today/observations?${params}`
}

function DailyCloseEvidence({ subject }: { subject: NonNullable<ObservationUpdate['primarySubject']> }) {
  const { t } = useI18n()
  if (subject.type !== 'instrument') return null
  if (subject.dailyCloseStatus === 'available' && subject.dailyClose)
    return <span> · {t('today.observations.dailyCloseEvidence', {
      date: subject.dailyClose.tradingDate,
      raw: subject.dailyClose.rawClose,
      adjusted: subject.dailyClose.adjustedClose,
    })}</span>
  return <span> · {subject.dailyCloseStatus === 'unsupported'
    ? t('today.observations.dailyCloseUnsupported')
    : t('today.observations.dailyCloseUnavailable')}</span>
}

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
  const { t, locale } = useI18n()
  const bootstrap = useBootstrapQuery()
  const observation = useTodayObservationQuery()
  const todayDiscipline = useTodayDisciplineQuery()
  const expectations = useExpectationsQuery()
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
  const [expectationUpdateId, setExpectationUpdateId] = useState<string | null>(null)
  const [editingExpectationId, setEditingExpectationId] = useState<string | null>(null)
  const [expectationHonestyReminder, setExpectationHonestyReminder] = useState(false)
  const [expectedBehavior, setExpectedBehavior] = useState('')
  const [invalidationCondition, setInvalidationCondition] = useState('')
  const [expectationConfidence, setExpectationConfidence] = useState<ExpectationConfidence>('medium')
  const [expectationMarket, setExpectationMarket] = useState('')
  const [expectationDeadlineMode, setExpectationDeadlineMode] = useState<'custom' | ExpectationDeadlinePreset>('custom')
  const [expectationDeadline, setExpectationDeadline] = useState('')
  const [expectationError, setExpectationError] = useState<string | null>(null)
  const [reviewExpectationId, setReviewExpectationId] = useState<string | null>(null)
  const idem = useIdempotencyKey()
  const expectationIdem = useIdempotencyKey()
  const saveQuickObservation = useQuickObservationMutation()
  const updateObservation = useUpdateObservationMutation()
  const createExpectation = useCreateExpectationMutation()
  const updateExpectation = useUpdateExpectationMutation()
  const invalidateExpectation = useInvalidateExpectationMutation()
  const observationTime = useMemo(() => new Intl.DateTimeFormat(locale, {
    hour: '2-digit', minute: '2-digit', hourCycle: 'h23', timeZone: observation.data?.timezone ?? 'UTC',
  }), [locale, observation.data?.timezone])
  const expectationItems = expectations.data ?? []
  const readyExpectations = expectationItems.filter(item => item.readiness === 'ready_for_review')
  const renderExpectation = (item: Expectation, actions = false) => {
    const sourceSubject = observation.data?.updates.find(update => update.id === item.observationUpdateId)?.primarySubject
    return <article className="stack">
    <h3>{item.expectedBehavior}</h3>
    <p><strong>{t('today.expectations.invalidation')}:</strong> {item.invalidationCondition}</p>
    <p><strong>{t('today.expectations.confidence')}:</strong> {item.confidence}</p>
    <p><strong>{t('today.expectations.market')}:</strong> {item.market}</p>
    <p><strong>{t('today.expectations.deadline')}:</strong> {item.deadline}</p>
    <p><strong>{t('today.expectations.readiness')}:</strong> {{
      active: t('today.expectations.readiness.active'),
      ready_for_review: t('today.expectations.readiness.ready'),
      reviewed: t('today.expectations.readiness.reviewed'),
    }[item.readiness]}</p>
    {sourceSubject ? <p><strong>{t('today.observations.dailyClose')}:</strong><DailyCloseEvidence subject={sourceSubject} /></p> : null}
    {actions ? <div className="form-actions">
      <Button variant="ghost" size="sm" onClick={() => beginExpectationEdit(item)}>{t('today.expectations.edit')}</Button>
      {item.readiness === 'active' ? <Button variant="ghost" size="sm" loading={invalidateExpectation.isPending} onClick={() => invalidateExpectation.mutate(item.id)}>{t('today.expectations.invalidate')}</Button> : null}
      {item.readiness !== 'active' ? <Button variant="ghost" size="sm" onClick={() => setReviewExpectationId(item.id)}>{item.readiness === 'reviewed' ? t('today.review.edit') : t('today.review.open')}</Button> : null}
    </div> : null}
  </article>
  }

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

  function beginExpectationEdit(item: Expectation) {
    setExpectationUpdateId(item.observationUpdateId)
    setEditingExpectationId(item.id)
    setExpectationHonestyReminder(item.deadlineElapsed)
    setExpectedBehavior(item.expectedBehavior)
    setInvalidationCondition(item.invalidationCondition)
    setExpectationConfidence(item.confidence)
    setExpectationMarket(item.market)
    setExpectationDeadlineMode('custom')
    setExpectationDeadline(utcToAccountDateTimeLocal(item.deadline, observation.data?.timezone ?? 'UTC'))
    setExpectationError(null)
  }

  function cancelExpectation() {
    setExpectationUpdateId(null)
    setEditingExpectationId(null)
    setExpectationHonestyReminder(false)
    setExpectedBehavior('')
    setInvalidationCondition('')
    setExpectationConfidence('medium')
    setExpectationMarket('')
    setExpectationDeadlineMode('custom')
    setExpectationDeadline('')
    setExpectationError(null)
  }

  async function saveExpectation() {
    if (!expectationUpdateId || !expectedBehavior.trim() || !invalidationCondition.trim() || !expectationMarket.trim()) return
    let deadline: string | null = null
    if (expectationDeadlineMode === 'custom') {
      const converted = accountDateTimeLocalToUtc(expectationDeadline, observation.data?.timezone ?? 'UTC')
      if (!converted.ok) {
        setExpectationError(converted.error === 'nonexistent' ? t('today.expectations.deadlineNonexistent') : t('today.expectations.deadlineInvalid'))
        return
      }
      deadline = converted.iso
    }
    const body = {
      expectedBehavior: expectedBehavior.trim(),
      deadline,
      deadlinePreset: expectationDeadlineMode === 'custom' ? null : expectationDeadlineMode,
      invalidationCondition: invalidationCondition.trim(),
      confidence: expectationConfidence,
      market: expectationMarket.trim().toUpperCase(),
    }
    if (editingExpectationId) await updateExpectation.mutateAsync({ id: editingExpectationId, body })
    else {
      await createExpectation.mutateAsync({ updateId: expectationUpdateId, key: expectationIdem.key(), body })
      expectationIdem.reset()
    }
    cancelExpectation()
  }

  const hour = new Date().getHours()
  const greeting = hour < 5
    ? t('today.greeting.late')
    : hour < 12
      ? t('today.greeting.morning')
      : hour < 18
        ? t('today.greeting.afternoon')
        : t('today.greeting.evening')

  return (
    <>
      <PageHeader title={greeting} subtitle={bootstrap.data ? formatLongDate(bootstrap.data.currentJournalDay) : undefined} />

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

      <div className="recent__head">
        <Link className="text-link" to="/today/observations">{t('observations.all')}</Link>
      </div>

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
                    {update.primarySubject ? <p><strong>{t('today.observations.primarySubject')}:</strong> <Link className="text-link" to={subjectHistoryHref(update.primarySubject)}>{subjectLabel(update.primarySubject)}</Link><DailyCloseEvidence subject={update.primarySubject} /></p> : null}
                    {update.relatedSubjects.map((subject, index) => <p key={`${subject.type}-${index}`}><strong>{t('today.observations.relatedSubject')}:</strong> <Link className="text-link" to={subjectHistoryHref(subject)}>{subjectLabel(subject)}</Link><DailyCloseEvidence subject={subject} /></p>)}
                    {update.tags.length ? <p><strong>{t('today.observations.tags')}:</strong> {update.tags.join(' · ')}</p> : null}
                    {update.evidence ? <><p><a href={update.evidence.url} target="_blank" rel="noreferrer">{update.evidence.title || update.evidence.url}</a></p>{update.evidence.quote ? <blockquote><strong>{t('today.observations.evidenceQuote')}:</strong> {update.evidence.quote}</blockquote> : null}</> : null}
                    <Button variant="ghost" size="sm" onClick={() => beginEdit(update)}>{t('today.observations.edit')}</Button>
                    <Button variant="ghost" size="sm" onClick={() => { cancelExpectation(); setExpectationUpdateId(update.id) }}>{t('today.expectations.add')}</Button>
                  </>
                )}
                {expectationUpdateId === update.id ? <div className="stack">
                  {expectationHonestyReminder ? <p className="form-hint" role="note">{t('today.expectations.honestyReminder')}</p> : null}
                  <Field label={t('today.expectations.expectedBehavior')}><TextArea value={expectedBehavior} onChange={event => setExpectedBehavior(event.target.value)} /></Field>
                  <Field label={t('today.expectations.invalidation')}><TextArea value={invalidationCondition} onChange={event => setInvalidationCondition(event.target.value)} /></Field>
                  <Field label={t('today.expectations.confidence')}><SelectBox value={expectationConfidence} onChange={event => setExpectationConfidence(event.target.value as ExpectationConfidence)}>
                    <option value="low">{t('today.expectations.confidence.low')}</option>
                    <option value="medium">{t('today.expectations.confidence.medium')}</option>
                    <option value="high">{t('today.expectations.confidence.high')}</option>
                  </SelectBox></Field>
                  <Field label={t('today.expectations.market')}><TextInput value={expectationMarket} onChange={event => {
                    const market = event.target.value
                    setExpectationMarket(market)
                    if (market.trim().toUpperCase() !== 'US') setExpectationDeadlineMode('custom')
                  }} /></Field>
                  <Field label={t('today.expectations.deadlineType')}><SelectBox value={expectationDeadlineMode} onChange={event => setExpectationDeadlineMode(event.target.value as 'custom' | ExpectationDeadlinePreset)}>
                    <option value="custom">{t('today.expectations.deadlineCustom')}</option>
                    {expectationMarket.trim().toUpperCase() === 'US' ? <>
                      <option value="next_trading_day">{t('today.expectations.deadlineNextTradingDay')}</option>
                      <option value="five_trading_days">{t('today.expectations.deadlineFiveTradingDays')}</option>
                    </> : null}
                  </SelectBox></Field>
                  {expectationDeadlineMode === 'custom' ? <>
                    <Field label={t('today.expectations.deadline')}><TextInput type="datetime-local" value={expectationDeadline} onChange={event => setExpectationDeadline(event.target.value)} /></Field>
                    <p className="form-hint">{formatTimezoneLabel(observation.data?.timezone ?? 'UTC')}</p>
                  </> : null}
                  {expectationError ? <p role="alert">{expectationError}</p> : null}
                  <div className="form-actions">
                    <Button variant="primary" size="sm" loading={createExpectation.isPending || updateExpectation.isPending} disabled={!expectedBehavior.trim() || !invalidationCondition.trim() || !expectationMarket.trim() || (expectationDeadlineMode === 'custom' && !expectationDeadline)} onClick={saveExpectation}>{t('today.expectations.save')}</Button>
                    <Button variant="ghost" size="sm" onClick={cancelExpectation}>{t('common.cancel')}</Button>
                  </div>
                </div> : null}
                {expectationItems.some(item => item.observationUpdateId === update.id) ? <ul className="stack" aria-label={t('today.expectations.title')}>
                  {expectationItems.filter(item => item.observationUpdateId === update.id).map(item => <li key={item.id}>{renderExpectation(item, true)}</li>)}
                </ul> : null}
                <ActionDecisionPanel updateId={update.id} expectations={expectationItems.filter(item => item.observationUpdateId === update.id)} />
              </li>
            ))}
          </ol>
        </section>
      ) : null}

      {readyExpectations.length ? <section className="recent" aria-labelledby="ready-expectations-h">
        <div className="recent__head"><h2 id="ready-expectations-h">{t('today.expectations.ready')}</h2></div>
        <ul className="stack">{readyExpectations.map(item => <li key={item.id}>{renderExpectation(item, true)}</li>)}</ul>
      </section> : null}

      {reviewExpectationId ? <ExpectationReviewForm expectationId={reviewExpectationId} onClose={() => setReviewExpectationId(null)} /> : null}

      <Card className="panel" as="section">
        <span className="panel__label">{t("today.discipline.label")}</span>
        <div className="panel__body">
          {todayDiscipline.data ? <blockquote className="panel__quote">{todayDiscipline.data.content}</blockquote> :
            !todayDiscipline.isError ? <p className="panel__sub">{t("today.discipline.empty")}</p> :
              <p className="panel__sub is-muted">{t("common.unavailable")}</p>}
        </div>
        <PanelLink onClick={() => go("review")}>{t("today.discipline.manage")}</PanelLink>
      </Card>

      {bootstrap.isError || observation.isError || expectations.isError ? <SectionError onRetry={() => { void bootstrap.refetch(); void observation.refetch(); void expectations.refetch() }} /> : null}
    </>
  )
}

function ActionDecisionPanel({ updateId, expectations }: { updateId: string; expectations: Expectation[] }) {
  const { t } = useI18n()
  const decisions = useActionDecisionsQuery(updateId)
  const create = useCreateActionDecisionMutation(updateId)
  const update = useUpdateActionDecisionMutation(updateId)
  const remove = useDeleteActionDecisionMutation(updateId)
  const [editing, setEditing] = useState<string | 'new' | null>(null)
  const [intent, setIntent] = useState<ActionDecisionIntent>('continue_observing')
  const [reason, setReason] = useState('')
  const [expectationId, setExpectationId] = useState('')
  const [executionReview, setExecutionReview] = useState<'' | NonNullable<ExecutionReview>>('')
  const [honestyReminder, setHonestyReminder] = useState(false)
  const [tradeDecisionId, setTradeDecisionId] = useState<string | null>(null)

  function begin(item?: ActionDecision) {
    setEditing(item?.id ?? 'new')
    setIntent(item?.intent ?? 'continue_observing')
    setReason(item?.reason ?? '')
    setExpectationId(item?.expectationId ?? '')
    setExecutionReview(item?.executionReview ?? '')
    setHonestyReminder(!!item)
  }

  async function save() {
    if (!reason.trim()) return
    const body = { intent, reason: reason.trim(), expectationId: expectationId || null, executionReview: executionReview || null }
    if (editing === 'new') await create.mutateAsync(body)
    else if (editing) await update.mutateAsync({ id: editing, body })
    setEditing(null)
  }

  return <section className="stack" aria-label={t('today.decisions.title')}>
    <div className="form-actions">
      <strong>{t('today.decisions.title')}</strong>
      {!editing ? <Button variant="ghost" size="sm" onClick={() => begin()}>{t('today.decisions.add')}</Button> : null}
    </div>
    {editing ? <div className="stack">
      {honestyReminder ? <p className="form-hint" role="note">{t('today.decisions.honestyReminder')}</p> : null}
      <Field label={t('today.decisions.intent')}><SelectBox value={intent} onChange={event => setIntent(event.target.value as ActionDecisionIntent)}>
        <option value="trade">{t('today.decisions.intent.trade')}</option>
        <option value="continue_observing">{t('today.decisions.intent.continueObserving')}</option>
        <option value="avoid_trade">{t('today.decisions.intent.avoidTrade')}</option>
      </SelectBox></Field>
      <Field label={t('today.decisions.reason')}><TextArea value={reason} onChange={event => setReason(event.target.value)} /></Field>
      <Field label={t('today.decisions.expectation')}><SelectBox value={expectationId} onChange={event => setExpectationId(event.target.value)}>
        <option value="">{t('common.optional')}</option>
        {expectations.map(item => <option key={item.id} value={item.id}>{item.expectedBehavior}</option>)}
      </SelectBox></Field>
      {editing !== 'new' ? <Field label={t('today.decisions.execution')}><SelectBox value={executionReview} onChange={event => setExecutionReview(event.target.value as '' | NonNullable<ExecutionReview>)}>
        <option value="">{t('common.optional')}</option>
        <option value="followed">{t('today.decisions.execution.followed')}</option>
        <option value="partially_followed">{t('today.decisions.execution.partiallyFollowed')}</option>
        <option value="deviated">{t('today.decisions.execution.deviated')}</option>
      </SelectBox></Field> : null}
      <div className="form-actions">
        <Button variant="primary" size="sm" loading={create.isPending || update.isPending} disabled={!reason.trim()} onClick={save}>{t('common.save')}</Button>
        <Button variant="ghost" size="sm" onClick={() => setEditing(null)}>{t('common.cancel')}</Button>
      </div>
    </div> : null}
    {(decisions.data ?? []).map(item => <article className="stack" key={item.id}>
      <p><strong>{{
        trade: t('today.decisions.intent.trade'),
        continue_observing: t('today.decisions.intent.continueObserving'),
        avoid_trade: t('today.decisions.intent.avoidTrade'),
      }[item.intent]}</strong> · <time dateTime={item.recordedAt}>{new Date(item.recordedAt).toLocaleString()}</time></p>
      <p>{item.reason}</p>
      {item.executionReview ? <p>{t('today.decisions.execution')}: {{
        followed: t('today.decisions.execution.followed'),
        partially_followed: t('today.decisions.execution.partiallyFollowed'),
        deviated: t('today.decisions.execution.deviated'),
      }[item.executionReview]}</p> : null}
      <TradeEvidenceList decisionId={item.id} />
      <div className="form-actions">
        <Button variant="ghost" size="sm" onClick={() => begin(item)}>{t('common.edit')}</Button>
        <Button variant="ghost" size="sm" onClick={() => setTradeDecisionId(item.id)}>{t('today.decisions.addTrade')}</Button>
        <Button variant="danger" size="sm" loading={remove.isPending} onClick={() => remove.mutate(item.id)}>{t('common.delete')}</Button>
      </div>
      {tradeDecisionId === item.id ? <TradeEvidenceForm decisionId={item.id} onClose={() => setTradeDecisionId(null)} /> : null}
    </article>)}
  </section>
}

function TradeEvidenceList({ decisionId }: { decisionId: string }) {
  const { t } = useI18n()
  const trades = useTradeEvidenceQuery(decisionId)
  return (trades.data ?? []).length ? <ul>
    {trades.data!.map(item => <li key={item.id}>{t('today.decisions.tradeSummary', { side: item.side, quantity: item.quantity, symbol: item.symbol, price: item.price, currency: item.currency })}</li>)}
  </ul> : null
}

function TradeEvidenceForm({ decisionId, onClose }: { decisionId: string; onClose: () => void }) {
  const { t } = useI18n()
  const create = useCreateTradeEvidenceMutation(decisionId)
  const [symbol, setSymbol] = useState('')
  const [side, setSide] = useState<'buy' | 'sell'>('buy')
  const [quantity, setQuantity] = useState('')
  const [price, setPrice] = useState('')
  const [currency, setCurrency] = useState('USD')
  const [executedAt, setExecutedAt] = useState('')
  const [note, setNote] = useState('')

  async function save() {
    if (!symbol.trim() || !quantity || !price || !executedAt) return
    await create.mutateAsync({
      symbol: symbol.trim(), side, quantity: Number(quantity), price: Number(price), currency: currency.trim(),
      executedAt: new Date(executedAt).toISOString(), note: note.trim() || null,
    })
    onClose()
  }

  return <div className="stack">
    <p className="form-hint">{t('today.decisions.tradeHint')}</p>
    <Field label={t('today.decisions.symbol')}><TextInput value={symbol} onChange={event => setSymbol(event.target.value)} /></Field>
    <Field label={t('today.decisions.side')}><SelectBox value={side} onChange={event => setSide(event.target.value as 'buy' | 'sell')}>
      <option value="buy">{t('today.decisions.side.buy')}</option><option value="sell">{t('today.decisions.side.sell')}</option>
    </SelectBox></Field>
    <Field label={t('today.decisions.quantity')}><TextInput type="number" min="0" step="any" value={quantity} onChange={event => setQuantity(event.target.value)} /></Field>
    <Field label={t('today.decisions.price')}><TextInput type="number" min="0" step="any" value={price} onChange={event => setPrice(event.target.value)} /></Field>
    <Field label={t('today.decisions.currency')}><TextInput value={currency} onChange={event => setCurrency(event.target.value)} /></Field>
    <Field label={t('today.decisions.executedAt')}><TextInput type="datetime-local" value={executedAt} onChange={event => setExecutedAt(event.target.value)} /></Field>
    <Field label={t('today.decisions.note')}><TextArea value={note} onChange={event => setNote(event.target.value)} /></Field>
    <div className="form-actions">
      <Button variant="primary" size="sm" loading={create.isPending} disabled={!symbol.trim() || !quantity || !price || !executedAt} onClick={save}>{t('common.save')}</Button>
      <Button variant="ghost" size="sm" onClick={onClose}>{t('common.cancel')}</Button>
    </div>
  </div>
}

function ExpectationReviewForm({ expectationId, onClose }: { expectationId: string; onClose: () => void }) {
  const { t } = useI18n()
  const existing = useExpectationReviewQuery(expectationId)
  const labels = useReasoningLabelsQuery()
  const save = useSaveExpectationReviewMutation(expectationId)
  const createLabel = useCreateReasoningLabelMutation()
  const [outcome, setOutcome] = useState<ExpectationReviewWrite['outcome']>('confirmed')
  const [quality, setQuality] = useState<ExpectationReviewWrite['reasoningQuality']>('sound')
  const [explanation, setExplanation] = useState('')
  const [selected, setSelected] = useState<string[]>([])
  const [customKind, setCustomKind] = useState<ReasoningLabelKind>('issue')
  const [customName, setCustomName] = useState('')

  useEffect(() => {
    if (!existing.data) return
    setOutcome(existing.data.outcome)
    setQuality(existing.data.reasoningQuality)
    setExplanation(existing.data.explanation ?? '')
    setSelected(existing.data.labels.map(label => label.id ?? label.key))
  }, [existing.data])

  const needsExplanation = outcome === 'partially_confirmed' || outcome === 'indeterminate'
  const toggle = (key: string) => setSelected(current => current.includes(key) ? current.filter(item => item !== key) : [...current, key])

  async function submit() {
    if (needsExplanation && !explanation.trim()) return
    const chosen = (labels.data ?? []).filter(label => selected.includes(label.id ?? label.key))
    await save.mutateAsync({
      outcome,
      reasoningQuality: quality,
      explanation: explanation.trim() || null,
      systemIssueKeys: chosen.filter(label => label.isSystem && label.kind === 'issue').map(label => label.key),
      systemStrengthKeys: chosen.filter(label => label.isSystem && label.kind === 'strength').map(label => label.key),
      customLabelIds: chosen.filter(label => !label.isSystem).map(label => label.id!),
    })
    onClose()
  }

  async function addCustomLabel() {
    if (!customName.trim()) return
    const created = await createLabel.mutateAsync({ kind: customKind, name: customName.trim() })
    setSelected(current => [...current, created.id!])
    setCustomName('')
  }

  return <Card as="section" className="stack">
    <h2>{t('today.review.title')}</h2>
    <Field label={t('today.review.outcome')}><SelectBox value={outcome} onChange={event => setOutcome(event.target.value as ExpectationReviewWrite['outcome'])}>
      <option value="confirmed">{t('today.review.outcome.confirmed')}</option>
      <option value="partially_confirmed">{t('today.review.outcome.partiallyConfirmed')}</option>
      <option value="invalidated">{t('today.review.outcome.invalidated')}</option>
      <option value="indeterminate">{t('today.review.outcome.indeterminate')}</option>
    </SelectBox></Field>
    <Field label={t('today.review.quality')}><SelectBox value={quality} onChange={event => setQuality(event.target.value as ExpectationReviewWrite['reasoningQuality'])}>
      <option value="sound">{t('today.review.quality.sound')}</option>
      <option value="mixed">{t('today.review.quality.mixed')}</option>
      <option value="weak">{t('today.review.quality.weak')}</option>
    </SelectBox></Field>
    <Field label={`${t('today.review.explanation')}${needsExplanation ? '' : ` (${t('common.optional')})`}`}>
      <TextArea value={explanation} onChange={event => setExplanation(event.target.value)} />
    </Field>
    {needsExplanation && !explanation.trim() ? <p role="alert">{t('today.review.explanationRequired')}</p> : null}
    {(['issue', 'strength'] as const).map(kind => <fieldset className="stack" key={kind}>
      <legend>{kind === 'issue' ? t('today.review.issues') : t('today.review.strengths')}</legend>
      {(labels.data ?? []).filter(label => label.kind === kind).map(label => <label key={label.id ?? label.key}>
        <input type="checkbox" checked={selected.includes(label.id ?? label.key)} onChange={() => toggle(label.id ?? label.key)} /> {label.isSystem ? t(`reasoning.${label.key}` as Parameters<typeof t>[0]) : label.name}
      </label>)}
    </fieldset>)}
    <div className="stack">
      <h3>{t('today.review.customLabel')}</h3>
      <SelectBox value={customKind} onChange={event => setCustomKind(event.target.value as ReasoningLabelKind)}>
        <option value="issue">{t('today.review.issues')}</option>
        <option value="strength">{t('today.review.strengths')}</option>
      </SelectBox>
      <TextInput value={customName} onChange={event => setCustomName(event.target.value)} />
      <Button variant="ghost" size="sm" disabled={!customName.trim()} loading={createLabel.isPending} onClick={addCustomLabel}>{t('common.add')}</Button>
    </div>
    <div className="form-actions">
      <Button variant="primary" loading={save.isPending} disabled={needsExplanation && !explanation.trim()} onClick={submit}>{t('common.save')}</Button>
      <Button variant="ghost" onClick={onClose}>{t('common.cancel')}</Button>
    </div>
  </Card>
}

export function ObservationHistoryPage() {
  const { t } = useI18n()
  const [search, setSearch] = useSearchParams()
  const filters: ObservationSearchFilters = {
    query: search.get('query') || undefined,
    from: search.get('from') || undefined,
    to: search.get('to') || undefined,
    subjectType: (search.get('subjectType') as ObservationSearchFilters['subjectType']) || undefined,
    subject: search.get('subject') || undefined,
    instrumentId: search.get('instrumentId') || undefined,
    market: search.get('market') || undefined,
    symbol: search.get('symbol') || undefined,
    tag: search.get('tag') || undefined,
    author: search.get('author') || undefined,
  }
  const history = useObservationHistoryQuery(filters)
  const items = Array.from(new Map((history.data?.pages.flatMap(page => page.items) ?? []).map(item => [item.update.id, item])).values())

  function applyFilters(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    const next = new URLSearchParams()
    for (const name of ['query', 'from', 'to', 'subjectType', 'subject', 'instrumentId', 'market', 'symbol', 'tag', 'author']) {
      const value = String(form.get(name) ?? '').trim()
      if (value) next.set(name, value)
    }
    setSearch(next)
  }

  return <>
    <PageHeader title={t('observations.all')} subtitle={t('observations.retained')} />
    <Card as="section">
      <form key={search.toString()} className="diary-filters" onSubmit={applyFilters}>
        <Field label={t('observations.query')}><TextInput name="query" defaultValue={filters.query} /></Field>
        <Field label={t('observations.from')}><TextInput name="from" type="date" defaultValue={filters.from} /></Field>
        <Field label={t('observations.to')}><TextInput name="to" type="date" defaultValue={filters.to} /></Field>
        <Field label={t('observations.subjectType')}><SelectBox name="subjectType" defaultValue={filters.subjectType ?? ''}>
          <option value="">{t('observations.anySubject')}</option>
          <option value="broad_market">{t('today.observations.subject.broadMarket')}</option>
          <option value="sector">{t('today.observations.subject.sector')}</option>
          <option value="theme">{t('today.observations.subject.theme')}</option>
        </SelectBox></Field>
        <Field label={t('observations.subject')}><TextInput name="subject" defaultValue={filters.subject} /></Field>
        <Field label={t('observations.instrumentId')}><TextInput name="instrumentId" defaultValue={filters.instrumentId} /></Field>
        <Field label={t('today.observations.market')}><TextInput name="market" defaultValue={filters.market} /></Field>
        <Field label={t('today.observations.symbol')}><TextInput name="symbol" defaultValue={filters.symbol} /></Field>
        <Field label={t('today.observations.tags')}><TextInput name="tag" defaultValue={filters.tag} /></Field>
        <Field label={t('observations.author')}><TextInput name="author" defaultValue={filters.author} placeholder="current" /></Field>
        <Button variant="primary" type="submit">{t('observations.search')}</Button>
      </form>
    </Card>
    {history.isLoading ? <PageSkeleton rows={3} /> : history.isError && !history.data ? <SectionError onRetry={() => { void history.refetch() }} /> : items.length === 0 ? (
      <EmptyBox icon="diary" title={t('observations.empty')} hint={t('observations.emptyHint')} />
    ) : <ol className="timeline">
      {items.map(item => <li key={item.update.id}>
        <time dateTime={item.update.recordedAt}>{item.journalDay}</time>
        <p>{item.update.content}</p>
        {item.update.primarySubject ? <p><Link className="text-link" to={subjectHistoryHref(item.update.primarySubject)}>{subjectLabel(item.update.primarySubject)}</Link><DailyCloseEvidence subject={item.update.primarySubject} /></p> : null}
        {item.update.relatedSubjects.map((subject, index) => <p key={`${subject.type}-${index}`}><Link className="text-link" to={subjectHistoryHref(subject)}>{subjectLabel(subject)}</Link><DailyCloseEvidence subject={subject} /></p>)}
        {item.update.tags.length ? <p>{item.update.tags.join(' · ')}</p> : null}
      </li>)}
    </ol>}
    {history.hasNextPage ? <Button variant="ghost" loading={history.isFetchingNextPage} onClick={() => { void history.fetchNextPage() }}>{t('observations.loadMore')}</Button> : null}
    {history.isFetchNextPageError ? <div><p className="form-error" role="alert">{t('observations.moreError')}</p><Button variant="ghost" onClick={() => { void history.fetchNextPage() }}>{t('common.retry')}</Button></div> : null}
  </>
}

/* =============================== CALENDAR ============================== */
export function CalendarPage() {
  const navigate = useNavigate()
  const params = useParams()
  const { t } = useI18n()
  const bootstrap = useBootstrapQuery()
  const accountToday = bootstrap.data?.currentJournalDay
  const year = Number(params.year) || (accountToday ? Number(accountToday.slice(0, 4)) : new Date().getFullYear())
  const month = Number(params.month) || (accountToday ? Number(accountToday.slice(5, 7)) : new Date().getMonth() + 1)
  const cursor = { year, month }
  const [search, setSearch] = useSearchParams()
  const { data, isLoading: loading, isError: error, refetch: reload } = useCalendarQuery(year, month)
  const requestedDay = search.get('day')
  const defaultDay = accountToday && validCalendarDay(accountToday, year, month) ? accountToday : `${year}-${String(month).padStart(2, '0')}-01`
  const selectedFromUrl = validCalendarDay(requestedDay, year, month) ? requestedDay! : defaultDay
  const [selected, setSelected] = useState(selectedFromUrl)
  useEffect(() => { setSelected(selectedFromUrl) }, [selectedFromUrl])
  const day = data?.days.find(item => item.date === selected)
  const firstWeekday = new Date(cursor.year, cursor.month - 1, 1).getDay()

  const shift = (delta: number) => {
    const date = new Date(cursor.year, cursor.month - 1 + delta, 1)
    navigate(`/calendar/${date.getFullYear()}/${String(date.getMonth() + 1).padStart(2, '0')}`)
  }

  return <>
    <PageHeader title={t('calendar.title')} subtitle={t('calendar.observationSubtitle')} />
    <div className="cal-head">
      <IconButton icon="left" label={t('calendar.prevMonth')} onClick={() => shift(-1)} />
      <h2 className="cal-head__title">{monthLabel(cursor.year, cursor.month)}</h2>
      <IconButton icon="right" label={t('calendar.nextMonth')} onClick={() => shift(1)} />
    </div>
    <Link className="text-link cal-review-link" to="/review">{t('calendar.reviewMonth')}</Link>
    {error ? <SectionError onRetry={reload} /> : <Card flush as="section" className="cal">
      <div className="cal__weekdays">{WEEKDAYS.map(key => <span key={key}>{t(key)}</span>)}</div>
      <div className="cal__grid">
        {loading ? Array.from({ length: 35 }, (_, index) => <span key={index} className="day day--skel"><span className="skel" style={{ height: '100%' }} /></span>) : <>
          {Array.from({ length: firstWeekday }, (_, index) => <span key={`b${index}`} className="day day--blank" />)}
          {data?.days.map(item => {
            const label = item.updateCount > 0 ? t('calendar.day.observations', { count: item.updateCount }) : t('calendar.day.noObservations')
            return <button key={item.date} type="button" className={cx('day', selected === item.date && 'is-selected')} aria-label={`${formatDate(item.date)}, ${label}`} onClick={() => {
              setSelected(item.date)
              const next = new URLSearchParams(search); next.set('day', item.date); setSearch(next)
            }}>
              <span className="day__num num">{Number(item.date.slice(-2))}</span>
              <span className={cx('day__pnl', item.updateCount === 0 && 'is-dash')}>{item.updateCount || '·'}</span>
              {item.updateCount > 0 ? <span className="day__note" aria-label={label} /> : null}
            </button>
          })}
        </>}
      </div>
    </Card>}
    <Card as="section">
      <h2>{selected}</h2>
      {day?.updateCount ? <>
        <p>{t('calendar.day.observations', { count: day.updateCount })}</p>
        <Link className="text-link" to={`/today/observations?from=${selected}&to=${selected}`}>{t('calendar.openObservations')}</Link>
        {day.readyForReviewCount != null ? <p>{t('calendar.readyForReview', { count: day.readyForReviewCount })}</p> : null}
      </> : <p className="form-hint">{t('calendar.day.noObservations')}</p>}
    </Card>
  </>
}

function validCalendarDay(value: string | null, year: number, month: number): boolean {
  if (!value || !/^\d{4}-\d{2}-\d{2}$/.test(value)) return false
  const date = new Date(`${value}T00:00:00Z`)
  return !Number.isNaN(date.getTime()) && date.getUTCFullYear() === year && date.getUTCMonth() + 1 === month && date.toISOString().slice(0, 10) === value
}

/* =============================== REVIEW ============================== */
export function ReviewPage() {
  const { t } = useI18n()
  const { data, isLoading: loading, isError: error, refetch: reload } = useDisciplinesQuery()
  const items = data?.items ?? []
  const [content, setContent] = useState('')
  const [formError, setFormError] = useState('')
  const [range, setRange] = useState<'weekly' | 'monthly' | 'custom'>('weekly')
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [reviewExpectationId, setReviewExpectationId] = useState<string | null>(null)
  const expectations = useExpectationsQuery()
  const reviewable = (expectations.data ?? []).filter(item => item.readiness !== 'active')
  const patterns = usePatternReviewQuery(range, from, to)
  const createDiscipline = useCreateDisciplineMutation()
  const updateDiscipline = useUpdateDisciplineMutation()
  const selectDiscipline = useSelectDisciplineMutation()

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

  const setStatus = (principle: Discipline, status: DisciplinePrincipleStatus) =>
    updateDiscipline.mutate({ id: principle.id, content: principle.content, status })

  return (
    <>
      <PageHeader title={t('review.title')} subtitle={t('review.subtitle')} />

      <ComparisonPanel />

      <Card as="section" className="stack">
        <h2>{t('review.expectations')}</h2>
        {expectations.isLoading ? <p>{t('common.loading')}</p> :
          expectations.isError ? <SectionError onRetry={() => { void expectations.refetch() }} /> :
          reviewable.length === 0 ? <p className="form-hint">{t('review.expectationsEmpty')}</p> :
          <ol className="principle-list">{reviewable.map(expectation => <li key={expectation.id}>
            <article className="stack">
              <strong>{expectation.expectedBehavior}</strong>
              <span>{t('today.expectations.confidence')}: {expectation.confidence}</span>
              <span>{t('today.expectations.readiness')}: {expectation.readiness}</span>
              <Button variant="ghost" size="sm" onClick={() => setReviewExpectationId(expectation.id)}>
                {expectation.readiness === 'reviewed' ? t('today.review.edit') : t('today.review.open')}
              </Button>
            </article>
          </li>)}</ol>}
      </Card>

      {reviewExpectationId ? <ExpectationReviewForm expectationId={reviewExpectationId} onClose={() => setReviewExpectationId(null)} /> : null}

      <Card as="section" className="stack">
        <h2>{t('discipline.patterns.title')}</h2>
        <div className="form-actions">
          {(['weekly', 'monthly', 'custom'] as const).map(value =>
            <Button key={value} variant={range === value ? 'subtle' : 'ghost'} size="sm" onClick={() => setRange(value)}>
              {t(`discipline.patterns.${value}`)}
            </Button>)}
        </div>
        {range === 'custom' ? <div className="form-grid">
          <Field label={t('discipline.patterns.from')}><TextInput type="date" value={from} onChange={event => setFrom(event.target.value)} /></Field>
          <Field label={t('discipline.patterns.to')}><TextInput type="date" value={to} onChange={event => setTo(event.target.value)} /></Field>
        </div> : null}
        {patterns.isError ? <SectionError onRetry={() => { void patterns.refetch() }} /> : patterns.isLoading ? <p>{t('common.loading')}</p> : patterns.data ? <>
          <p>{t('discipline.patterns.reviewed', { count: patterns.data.reviewedExpectationCount })}</p>
          {Number(patterns.data.reviewedExpectationCount) === 0 ? <p className="form-hint">{t('discipline.patterns.empty')}</p> :
            <ol className="principle-list">
              {patterns.data.labels.filter(label => Number(label.count) > 0).map(label => <li key={`${label.kind}:${label.key}`}>
                <strong>{label.name}</strong> · {label.count}/{label.denominator}
                <span> {label.evidence.map((evidence, index) =>
                  <Link key={evidence.expectationId} className="text-link" to={evidence.url}>{t('discipline.patterns.evidence', { count: index + 1 })}</Link>)}</span>
              </li>)}
            </ol>}
        </> : null}
      </Card>

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
                <div className="form-actions">
                  {d.status === 'active' && !d.selectedForToday ? <Button size="sm" variant="subtle" onClick={() => selectDiscipline.mutate(d.id)}>{t('discipline.select')}</Button> : null}
                  {d.selectedForToday ? <Badge tone="gain">{t('discipline.selected')}</Badge> : null}
                  {d.status === 'active' ? <Button size="sm" variant="ghost" onClick={() => setStatus(d, 'disabled')}>{t('discipline.disable')}</Button> : null}
                  {d.status === 'disabled' ? <Button size="sm" variant="ghost" onClick={() => setStatus(d, 'active')}>{t('discipline.enable')}</Button> : null}
                  {d.status !== 'archived' ? <Button size="sm" variant="danger" onClick={() => setStatus(d, 'archived')}>{t('discipline.archive')}</Button> : <Badge tone="muted">{t('discipline.archived')}</Badge>}
                </div>
              </Card>
            </li>
          ))}
        </ol>
      )}
    </>
  )
}

function ComparisonPanel() {
  const { t } = useI18n()
  const agents = useAgentsQuery()
  const instruments = useInstrumentDirectoryQuery()
  const today = new Date().toISOString().slice(0, 10)
  const [agentUserId, setAgentUserId] = useState('')
  const [subjectType, setSubjectType] = useState<'theme' | 'sector' | 'broad_market' | 'instrument'>('theme')
  const [subject, setSubject] = useState('')
  const [instrumentId, setInstrumentId] = useState('')
  const [from, setFrom] = useState(`${today.slice(0, 7)}-01`)
  const [to, setTo] = useState(today)
  const [query, setQuery] = useState<ComparisonQuery | null>(null)
  const comparison = useComparisonQuery(query)
  const agentName = agents.data?.items.find(agent => agent.userId === query?.agentUserId)?.displayName ?? t('comparison.agent')
  const valid = !!agentUserId && !!from && !!to && from <= to
    && (subjectType === 'instrument' ? !!instrumentId : !!subject.trim())

  function submit(event: FormEvent) {
    event.preventDefault()
    if (!valid) return
    setQuery(subjectType === 'instrument'
      ? { agentUserId, from, to, instrumentId }
      : { agentUserId, from, to, subjectType, subject: subject.trim() })
  }

  return <Card as="section" className="stack" aria-labelledby="comparison-title">
    <div>
      <h2 id="comparison-title">{t('comparison.title')}</h2>
      <p className="form-hint">{t('comparison.subtitle')}</p>
    </div>
    <form className="comparison-filters" onSubmit={submit}>
      <Field label={t('comparison.agent')}>
        <SelectBox value={agentUserId} onChange={event => setAgentUserId(event.target.value)} required>
          <option value="">{t('comparison.chooseAgent')}</option>
          {(agents.data?.items ?? []).map(agent => <option key={agent.userId} value={agent.userId}>{agent.displayName}</option>)}
        </SelectBox>
      </Field>
      <Field label={t('comparison.subjectType')}>
        <SelectBox value={subjectType} onChange={event => setSubjectType(event.target.value as typeof subjectType)}>
          <option value="theme">{t('today.observations.subject.theme')}</option>
          <option value="sector">{t('today.observations.subject.sector')}</option>
          <option value="broad_market">{t('today.observations.subject.broadMarket')}</option>
          <option value="instrument">{t('today.observations.subject.instrument')}</option>
        </SelectBox>
      </Field>
      {subjectType === 'instrument' ? <Field label={t('comparison.instrument')}>
        <SelectBox value={instrumentId} onChange={event => setInstrumentId(event.target.value)} required>
          <option value="">{t('comparison.chooseInstrument')}</option>
          {(instruments.data ?? []).map(instrument => <option key={instrument.instrumentId} value={instrument.instrumentId}>{instrument.symbol} · {instrument.name}</option>)}
        </SelectBox>
      </Field> : <Field label={t('comparison.subject')}>
        <TextInput value={subject} onChange={event => setSubject(event.target.value)} required maxLength={120} />
      </Field>}
      <Field label={t('comparison.from')}><TextInput type="date" value={from} onChange={event => setFrom(event.target.value)} required /></Field>
      <Field label={t('comparison.to')}><TextInput type="date" value={to} onChange={event => setTo(event.target.value)} required /></Field>
      <Button variant="primary" type="submit" disabled={!valid}>{t('comparison.open')}</Button>
    </form>
    {agents.isError ? <p role="alert">{t('comparison.agentsUnavailable')}</p> : null}
    {query && comparison.isLoading ? <p role="status">{t('common.loading')}</p> : null}
    {comparison.isError ? <SectionError onRetry={() => { void comparison.refetch() }} /> : null}
    {comparison.data ? <ComparisonResult comparison={comparison.data} agentName={agentName} /> : null}
  </Card>
}

function ComparisonResult({ comparison, agentName }: { comparison: OwnerComparison; agentName: string }) {
  const { t } = useI18n()
  const outcome = comparison.difference.outcomeConsistent == null
    ? t('comparison.unavailable')
    : comparison.difference.outcomeConsistent ? t('comparison.same') : t('comparison.different')
  const confidence = comparison.difference.confidenceDifference == null
    ? t('comparison.unavailable')
    : Number(comparison.difference.confidenceDifference) === 0
      ? t('comparison.same')
      : t('comparison.confidenceDifference', { value: Number(comparison.difference.confidenceDifference) > 0
        ? `+${comparison.difference.confidenceDifference}` : comparison.difference.confidenceDifference })
  return <div className="stack">
    <dl className="comparison-differences" aria-label={t('comparison.objectiveDifferences')}>
      <div><dt>{t('comparison.outcomeConsistency')}</dt><dd>{outcome}</dd></div>
      <div><dt>{t('comparison.confidence')}</dt><dd>{confidence}</dd></div>
    </dl>
    <div className="comparison-columns">
      <ComparisonOwner title={t('comparison.human')} owner={comparison.human} />
      <ComparisonOwner title={agentName} owner={comparison.agent} />
    </div>
  </div>
}

function ComparisonOwner({ title, owner }: { title: string; owner: OwnerComparison['human'] }) {
  const { t } = useI18n()
  return <section className="comparison-owner" aria-label={`${title} · ${t(`comparison.owner.${owner.ownerType}`)}`}>
    <h3>{title}</h3>
    <p className="comparison-owner__label">{t(`comparison.owner.${owner.ownerType}`)}</p>
    {owner.availability === 'unavailable' ? <p className="form-hint">{t('comparison.grantUnavailable')}</p>
      : owner.observations.length === 0 ? <p className="form-hint">{t('comparison.empty')}</p>
        : <ol className="comparison-records">{owner.observations.map(observation => <li key={observation.update.id}>
          <article className="stack">
            <time dateTime={observation.update.recordedAt}>{observation.journalDay}</time>
            <p>{observation.update.content}</p>
            {observation.update.primarySubject ? <p>{subjectLabel(observation.update.primarySubject)}<DailyCloseEvidence subject={observation.update.primarySubject} /></p> : null}
            {observation.expectations.length === 0 ? <p className="form-hint">{t('comparison.expectationUnavailable')}</p>
              : observation.expectations.map(expectation => <dl key={expectation.id} className="comparison-expectation">
                <div><dt>{t('today.expectations.expectedBehavior')}</dt><dd>{expectation.expectedBehavior}</dd></div>
                <div><dt>{t('today.expectations.confidence')}</dt><dd>{expectation.confidence}</dd></div>
                <div><dt>{t('today.review.outcome')}</dt><dd>{expectation.outcome ?? t('comparison.unavailable')}</dd></div>
                <div><dt>{t('today.review.quality')}</dt><dd>{expectation.reasoningQuality ?? t('comparison.unavailable')}</dd></div>
              </dl>)}
          </article>
        </li>)}</ol>}
  </section>
}
