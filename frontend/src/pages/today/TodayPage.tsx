import { useMemo, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import type { Expectation, ExpectationConfidence, ExpectationDeadlinePreset, ObservationSubjectWrite, ObservationUpdate } from '../../features/api'
import { useIdempotencyKey } from '../../features/api'
import {
  useBootstrapQuery,
  useCreateExpectationMutation,
  useExpectationsQuery,
  useInstrumentDirectoryQuery,
  useInvalidateExpectationMutation,
  useQuickObservationMutation,
  useTodayDisciplineQuery,
  useTodayObservationQuery,
  useObservationHistoryQuery,
  useUpdateExpectationMutation,
  useUpdateObservationMutation,
} from '../../features/queries'
import { Badge, Button, Card, EmptyBox, Field, IconButton, PageHeader, SelectBox, TextArea, TextInput } from '../../ui'
import { Icon } from '../../icons'
import { SectionError, useCockpit } from '../../shell'
import { cx, formatLongDate } from '../../format'
import { accountDateTimeLocalToUtc, accountLocalHour, formatTimezoneLabel, utcToAccountDateTimeLocal } from '../../features/accountTime'
import { useI18n } from '../../i18n'
import { ActionDecisionPanel } from './ActionDecisionPanel'
import { ExpectationReviewForm } from './ExpectationReviewForm'
import {
  DailyCloseEvidence,
  PanelLink,
  SubjectFields,
  emptySubject,
  subjectDraft,
  subjectHistoryHref,
  subjectLabel,
  subjectWrite,
} from '../shared'

function observationCopy(content: string) {
  const lines = content.trim().split(/\n+/).map(line => line.trim()).filter(Boolean)
  const title = lines[0] ?? content.trim()
  const preview = lines.slice(1).join(' ')
  return { title, preview }
}

function truncate(value: string, max = 150) {
  const trimmed = value.trim()
  return trimmed.length > max ? `${trimmed.slice(0, max).trimEnd()}…` : trimmed
}

export function TodayPage() {
  const { go } = useCockpit()
  const { t, locale } = useI18n()
  const bootstrap = useBootstrapQuery()
  const observation = useTodayObservationQuery()
  const recentHistory = useObservationHistoryQuery({})
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
  const [editPrimarySubject, setEditPrimarySubject] = useState(emptySubject)
  const [editRelatedSubjects, setEditRelatedSubjects] = useState<ReturnType<typeof emptySubject>[]>([])
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
  const composerRef = useRef<HTMLTextAreaElement>(null)
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
  const recentUpdates = useMemo(() => {
    const currentDay = bootstrap.data?.currentJournalDay
    const items = recentHistory.data?.pages.flatMap(page => page.items) ?? []
    return items
      .filter(item => !currentDay || item.journalDay !== currentDay)
      .slice(0, 3)
  }, [bootstrap.data?.currentJournalDay, recentHistory.data?.pages])
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

  function focusComposer() {
    composerRef.current?.scrollIntoView({ behavior: 'smooth', block: 'center' })
    window.setTimeout(() => composerRef.current?.focus(), 180)
  }

  const hour = accountLocalHour(new Date(), bootstrap.data?.timezone)
  const greeting = hour < 5
    ? t('today.greeting.late')
    : hour < 12
      ? t('today.greeting.morning')
      : hour < 18
        ? t('today.greeting.afternoon')
        : t('today.greeting.evening')

  const displayName = bootstrap.data?.currentUser.displayName || bootstrap.data?.currentUser.email || ''
  return <>
    <PageHeader
      className="today-head"
      title={displayName ? `${greeting}, ${displayName}` : greeting}
      subtitle={bootstrap.data ? formatLongDate(bootstrap.data.currentJournalDay) : undefined}
      actions={<div className="today-head__actions">
        <Link className="today-head__search" to="/today/observations" aria-label={t('today.header.search')}>
          <Icon name="search" size={18} />
          <span>{t('today.header.search')}…</span>
          <kbd>⌘ K</kbd>
        </Link>
        <IconButton className="today-head__notify" icon="bell" label={t('nav.alerts')} disabled title={t('today.header.notificationsUnavailable')} />
        <Button variant="primary" icon="plus" onClick={focusComposer} aria-label={t('today.header.quickCreate')}>{t('today.header.quickCreate')}</Button>
      </div>}
    />
    <Card className="quick-note" as="section" id="composer">
      <div className="quick-note__head">
        <span className="quick-note__icon"><Icon name="edit" size={24} /></span>
        <div className="quick-note__copy">
          <h2 className="quick-note__label">{t('today.quickNote.label')}</h2>
          <p className="quick-note__description">{t('today.quickNote.description')}</p>
        </div>
      </div>
      <label className="visually-hidden" htmlFor="qn">{t('today.quickNote.label')}</label>
      <TextArea ref={composerRef} id="qn" value={note} onChange={event => setNote(event.target.value)} placeholder={t('today.quickNote.placeholder')} className="textarea--prose" />
      <div className="quick-note__foot">
        <span className={cx('quick-note__status', saved && 'is-ok')}>
          {saved ? <><Icon name="check" size={14} /> {t('common.saved')}</> : <><span>{t('today.quickNote.hint')}</span><span className="quick-note__count">{note.length}</span></>}
        </span>
        <Button variant="primary" icon="plus" loading={saveQuickObservation.isPending} onClick={saveNote} disabled={!note.trim()}>{t('today.quickNote.save')}</Button>
      </div>
    </Card>

    <section className="recent" aria-labelledby="observation-updates-h">
      <div className="recent__head">
        <div className="recent__heading"><h2 id="observation-updates-h">{t('today.observations.title')}</h2><span className="recent__meta">{t('today.observations.count', { count: observation.data?.updates.length ?? 0 })}</span></div>
        <Link className="text-link" to="/today/observations">{t('observations.all')}</Link>
      </div>
      {observation.data?.updates.length ? <ol className="observation-list">
        {observation.data.updates.map(update => <li key={update.id}>
          {editingId === update.id ? <div className="observation-card observation-card__edit"><div className="stack">
            <p className="form-hint" role="note">{t('today.observations.honestyReminder')}</p>
            <Field label={t('today.observations.editLabel')}><TextArea value={editContent} onChange={event => setEditContent(event.target.value)} /></Field>
            <Field label={t('today.observations.signal')}><TextArea value={editSignal} onChange={event => setEditSignal(event.target.value)} /></Field>
            <Field label={t('today.observations.interpretation')}><TextArea value={editInterpretation} onChange={event => setEditInterpretation(event.target.value)} /></Field>
            <Field label={t('today.observations.mentalState')}><TextInput value={editMentalState} onChange={event => setEditMentalState(event.target.value)} /></Field>
            <Field label={t('today.observations.tags')}><TextInput value={editTags} onChange={event => setEditTags(event.target.value)} /></Field>
            <SubjectFields subject={editPrimarySubject} onChange={setEditPrimarySubject} instruments={instruments.data ?? []} instrumentsLoading={instruments.isLoading} prefix="today.observations.primary" />
            {editRelatedSubjects.map((subject, index) => <div className="stack" key={index}>
              <SubjectFields subject={subject} instruments={instruments.data ?? []} instrumentsLoading={instruments.isLoading} prefix="today.observations.related" onChange={next => setEditRelatedSubjects(current => current.map((item, itemIndex) => itemIndex === index ? next : item))} />
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
          </div></div> : <div className="observation-card">
            <div className="observation-card__time"><span className="observation-card__status" aria-hidden="true" /><time dateTime={update.recordedAt}>{observationTime.format(new Date(update.recordedAt))}</time></div>
            <div className="observation-card__body">
              {(() => { const copy = observationCopy(update.content); return <><h3 className="observation-card__title">{copy.title}</h3>{copy.preview ? <p className="observation-card__preview">{copy.preview}</p> : null}</> })()}
              <div className="observation-card__meta">
                {update.primarySubject ? <><strong>{t('today.observations.primarySubject')}:</strong><Link className="text-link" to={subjectHistoryHref(update.primarySubject)}>{subjectLabel(update.primarySubject)}</Link></> : null}
                {update.tags.map(tag => <Badge key={tag} tone="primary">{tag}</Badge>)}
                {update.signal ? <Badge tone="muted">{t('today.observations.signal')}</Badge> : null}
                {update.interpretation ? <Badge tone="muted">{t('today.observations.interpretation')}</Badge> : null}
              </div>
            </div>
            <div className="observation-card__actions" aria-label={t('today.observations.actions')}>
              <Button variant="subtle" size="sm" icon="edit" onClick={() => beginEdit(update)}>{t('today.observations.edit')}</Button>
              <Button variant="subtle" size="sm" icon="plus" onClick={() => { cancelExpectation(); setExpectationUpdateId(update.id) }}>{t('today.expectations.add')}</Button>
            </div>
            {(update.signal || update.interpretation || update.mentalState || update.relatedSubjects.length || update.evidence) ? <div className="observation-card__detail stack">
              {update.signal ? <p><strong>{t('today.observations.signal')}:</strong> {update.signal}</p> : null}
              {update.interpretation ? <p><strong>{t('today.observations.interpretation')}:</strong> {update.interpretation}</p> : null}
              {update.mentalState ? <p><strong>{t('today.observations.mentalState')}:</strong> {update.mentalState}</p> : null}
              {update.relatedSubjects.map((subject, index) => <p key={`${subject.type}-${index}`}><strong>{t('today.observations.relatedSubject')}:</strong> <Link className="text-link" to={subjectHistoryHref(subject)}>{subjectLabel(subject)}</Link><DailyCloseEvidence subject={subject} /></p>)}
              {update.evidence ? <p><a href={update.evidence.url} target="_blank" rel="noreferrer">{update.evidence.title || update.evidence.url}</a>{update.evidence.quote ? <> · {update.evidence.quote}</> : null}</p> : null}
            </div> : null}
          </div>}
          {expectationUpdateId === update.id ? <div className="stack">
            {expectationHonestyReminder ? <p className="form-hint" role="note">{t('today.expectations.honestyReminder')}</p> : null}
            <Field label={t('today.expectations.expectedBehavior')}><TextArea value={expectedBehavior} onChange={event => setExpectedBehavior(event.target.value)} /></Field>
            <Field label={t('today.expectations.invalidation')}><TextArea value={invalidationCondition} onChange={event => setInvalidationCondition(event.target.value)} /></Field>
            <Field label={t('today.expectations.confidence')}><SelectBox value={expectationConfidence} onChange={event => setExpectationConfidence(event.target.value as ExpectationConfidence)}>
              <option value="low">{t('today.expectations.confidence.low')}</option><option value="medium">{t('today.expectations.confidence.medium')}</option><option value="high">{t('today.expectations.confidence.high')}</option>
            </SelectBox></Field>
            <Field label={t('today.expectations.market')}><TextInput value={expectationMarket} onChange={event => { const market = event.target.value; setExpectationMarket(market); if (market.trim().toUpperCase() !== 'US') setExpectationDeadlineMode('custom') }} /></Field>
            <Field label={t('today.expectations.deadlineType')}><SelectBox value={expectationDeadlineMode} onChange={event => setExpectationDeadlineMode(event.target.value as 'custom' | ExpectationDeadlinePreset)}>
              <option value="custom">{t('today.expectations.deadlineCustom')}</option>
              {expectationMarket.trim().toUpperCase() === 'US' ? <><option value="next_trading_day">{t('today.expectations.deadlineNextTradingDay')}</option><option value="five_trading_days">{t('today.expectations.deadlineFiveTradingDays')}</option></> : null}
            </SelectBox></Field>
            {expectationDeadlineMode === 'custom' ? <><Field label={t('today.expectations.deadline')}><TextInput type="datetime-local" value={expectationDeadline} onChange={event => setExpectationDeadline(event.target.value)} /></Field><p className="form-hint">{formatTimezoneLabel(observation.data?.timezone ?? 'UTC')}</p></> : null}
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
        </li>)}
      </ol> : <EmptyBox className="observation-empty" icon="diary" title={t('today.observations.emptyTitle')} hint={t('today.observations.emptyHint')} />}
    </section>

    {recentUpdates.length ? <section className="recent" aria-labelledby="recent-observations-h">
      <div className="recent__head"><div className="recent__heading"><h2 id="recent-observations-h">{t('today.recent.observations')}</h2><span className="recent__meta">{t('today.recent.observationsHint')}</span></div><Link className="text-link" to="/today/observations">{t('today.recent.viewAll')} <Icon name="arrow" size={14} /></Link></div>
      <div className="recent-grid">
        {recentUpdates.map(item => {
          const copy = observationCopy(item.update.content)
          return <article className="recent-card" key={item.update.id}>
            <div className="recent-card__head"><span className="recent-card__date">{formatLongDate(item.journalDay)}</span><time dateTime={item.update.recordedAt}>{observationTime.format(new Date(item.update.recordedAt))}</time></div>
            <h3 className="recent-card__title">{copy.title}</h3>
            <p className="recent-card__preview">{truncate(copy.preview || item.update.content)}</p>
            <div className="recent-card__foot">{item.update.primarySubject ? <Badge tone="primary">{subjectLabel(item.update.primarySubject)}</Badge> : null}{item.update.tags.slice(0, 1).map(tag => <Badge key={tag}>{tag}</Badge>)}</div>
          </article>
        })}
      </div>
    </section> : null}

    {readyExpectations.length ? <section className="recent" aria-labelledby="ready-expectations-h">
      <div className="recent__head"><h2 id="ready-expectations-h">{t('today.expectations.ready')}</h2></div>
      <ul className="stack">{readyExpectations.map(item => <li key={item.id}>{renderExpectation(item, true)}</li>)}</ul>
    </section> : null}
    {reviewExpectationId ? <ExpectationReviewForm expectationId={reviewExpectationId} onClose={() => setReviewExpectationId(null)} /> : null}
    <Card className="panel discipline-card" as="section">
      <span className="panel__label">{t('today.discipline.label')}</span>
      <div className="panel__body">
        {todayDiscipline.data ? <blockquote className="panel__quote">{todayDiscipline.data.content}</blockquote> : !todayDiscipline.isError ? <p className="panel__sub">{t('today.discipline.empty')}</p> : <p className="panel__sub is-muted">{t('common.unavailable')}</p>}
      </div>
      <PanelLink onClick={() => go('review')}>{t('today.discipline.manage')}</PanelLink>
    </Card>
    {bootstrap.isError || observation.isError || expectations.isError || recentHistory.isError ? <SectionError onRetry={() => { void bootstrap.refetch(); void observation.refetch(); void expectations.refetch(); void recentHistory.refetch() }} /> : null}
  </>
}
