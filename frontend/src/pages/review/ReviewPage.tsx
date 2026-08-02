import { useMemo, useState, type FormEvent } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import type { Discipline, DisciplinePrincipleStatus, Expectation, ObservationSearchItem } from '../../features/api'
import {
  useCreateDisciplineMutation,
  useDisciplinesQuery,
  useExpectationReviewQuery,
  useExpectationsQuery,
  useObservationHistoryQuery,
  usePatternReviewQuery,
  useSelectDisciplineMutation,
  useUpdateDisciplineMutation,
} from '../../features/queries'
import { Badge, Button, Card, EmptyBox, Field, PageHeader, TextInput } from '../../ui'
import { SectionError } from '../../shell'
import { useI18n } from '../../i18n'
import { ExpectationSelfReviewForm } from './ExpectationSelfReviewForm'
import { ComparisonPanel } from './ComparisonPanel'
import './ReviewPage.css'

function comparisonHref(expectation: Expectation, item: ObservationSearchItem | undefined) {
  const subject = item?.update.primarySubject
  if (!item || !subject) return null
  const from = item.journalDay
  const deadline = expectation.deadline.slice(0, 10)
  const to = deadline >= from ? deadline : from
  const params = new URLSearchParams({ from, to })
  if (subject.type === 'instrument') {
    if (!subject.instrumentId) return null
    params.set('instrumentId', subject.instrumentId)
  } else if (subject.name) {
    params.set('subjectType', subject.type)
    params.set('subject', subject.name)
  } else return null
  return `/review?${params.toString()}`
}

function reviewStatus(expectation: Expectation, t: ReturnType<typeof useI18n>['t']) {
  return expectation.readiness === 'reviewed' ? t('review.status.reviewed') : t('review.status.unreviewed')
}

function ReviewQueueItem({
  expectation,
  updates,
  onOpen,
}: {
  expectation: Expectation
  updates: Map<string, ObservationSearchItem>
  onOpen: () => void
}) {
  const { t, format } = useI18n()
  const reviewed = expectation.readiness === 'reviewed'
  const review = useExpectationReviewQuery(expectation.id, reviewed)
  const comparison = comparisonHref(expectation, updates.get(expectation.observationUpdateId))
  const relationshipHref = `/today/observations?from=${encodeURIComponent(expectation.journalDay)}&to=${encodeURIComponent(expectation.journalDay)}`

  return <li>
    <article className="review-queue__item">
      <header className="review-queue__item-header">
        <div>
          <p className="review-queue__target">{expectation.market}</p>
          <h3>{expectation.expectedBehavior}</h3>
        </div>
        <Badge tone={reviewed ? 'gain' : 'primary'}>{reviewStatus(expectation, t)}</Badge>
      </header>
      <dl className="review-queue__facts">
        <div><dt>{t('review.relationship')}</dt><dd><Link className="text-link" to={relationshipHref}>{format.date(expectation.journalDay)}</Link></dd></div>
        <div><dt>{t('today.expectations.confidence')}</dt><dd>{expectation.confidence}</dd></div>
        <div><dt>{t('review.created')}</dt><dd><time dateTime={expectation.createdAt}>{format.dateTime(expectation.createdAt)}</time></dd></div>
        {review.data?.createdAt ? <div><dt>{t('review.reviewedOn')}</dt><dd><time dateTime={review.data.createdAt}>{format.dateTime(review.data.createdAt)}</time></dd></div> : null}
      </dl>
      <div className="review-queue__actions">
        <Button variant="primary" size="sm" onClick={onOpen}>{reviewed ? t('review.selfReview.edit') : t('review.selfReview.start')}</Button>
        {comparison ? <Link className="text-link" to={comparison}>{t('comparison.open')}</Link> : null}
      </div>
    </article>
  </li>
}

export function ReviewPage() {
  const { t } = useI18n()
  const [searchParams] = useSearchParams()
  const [queue, setQueue] = useState<'pending' | 'completed'>('pending')
  const [comparisonOpen, setComparisonOpen] = useState(() => ['agentUserId', 'subjectType', 'subject', 'instrumentId', 'from', 'to', 'period'].some(key => searchParams.has(key)))
  const [reviewExpectationId, setReviewExpectationId] = useState<string | null>(null)
  const [range, setRange] = useState<'weekly' | 'monthly' | 'custom'>('weekly')
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [content, setContent] = useState('')
  const [formError, setFormError] = useState('')

  const expectations = useExpectationsQuery()
  const observationHistory = useObservationHistoryQuery({})
  const { data, isLoading: loading, isError: error, refetch: reload } = useDisciplinesQuery()
  const patterns = usePatternReviewQuery(range, from, to)
  const createDiscipline = useCreateDisciplineMutation()
  const updateDiscipline = useUpdateDisciplineMutation()
  const selectDiscipline = useSelectDisciplineMutation()
  const items = data?.items ?? []
  const expectationItems = expectations.data ?? []
  const pending = expectationItems.filter(item => item.readiness === 'ready_for_review')
  const completed = expectationItems.filter(item => item.readiness === 'reviewed')
  const visibleExpectations = queue === 'pending' ? pending : completed
  const selectedExpectation = expectationItems.find(item => item.id === reviewExpectationId)
  const updates = useMemo(() => new Map((observationHistory.data?.pages.flatMap(page => page.items) ?? []).map(item => [item.update.id, item])), [observationHistory.data?.pages])

  async function add(event: FormEvent) {
    event.preventDefault()
    setFormError('')
    try {
      await createDiscipline.mutateAsync(content.trim())
      setContent('')
    } catch {
      setFormError(t('discipline.addError'))
    }
  }

  const setStatus = (principle: Discipline, status: DisciplinePrincipleStatus) => updateDiscipline.mutate({ id: principle.id, content: principle.content, status })

  return <div className="review-page">
    <PageHeader title={t('review.title')} subtitle={t('review.subtitle')} />

    <Card as="section" className="review-queue" aria-labelledby="review-queue-title">
      <header className="review-queue__header">
        <div>
          <p className="eyebrow">{t('review.queue.eyebrow')}</p>
          <h2 id="review-queue-title">{t('review.queue.title')}</h2>
          <p className="form-hint">{t('review.queue.subtitle')}</p>
        </div>
        <div className="review-queue__counts" aria-label={t('review.queue.counts')}>
          <span><strong>{pending.length}</strong> {t('review.queue.pendingCount')}</span>
          <span><strong>{completed.length}</strong> {t('review.queue.completedCount')}</span>
        </div>
      </header>
      <div className="review-queue__tabs" role="tablist" aria-label={t('review.queue.tabs')}>
        <Button role="tab" aria-selected={queue === 'pending'} variant={queue === 'pending' ? 'subtle' : 'ghost'} size="sm" onClick={() => setQueue('pending')}>{t('review.queue.pendingTab')}</Button>
        <Button role="tab" aria-selected={queue === 'completed'} variant={queue === 'completed' ? 'subtle' : 'ghost'} size="sm" onClick={() => setQueue('completed')}>{t('review.queue.completedTab')}</Button>
      </div>
      {expectations.isLoading ? <div className="stack" role="status" aria-label={t('common.loading')}><div className="skel" style={{ height: 24, width: '75%' }} /><div className="skel" style={{ height: 18, width: '48%' }} /></div> : expectations.isError ? <SectionError onRetry={() => { void expectations.refetch() }} /> : visibleExpectations.length === 0 ? <EmptyBox icon="diary" dense title={queue === 'pending' ? t('review.queue.emptyTitle') : t('review.queue.completedEmptyTitle')} hint={queue === 'pending' ? t('review.queue.emptyHint') : t('review.queue.completedEmptyHint')} action={<div className="review-queue__empty-actions"><Link className="btn btn--primary btn--sm" to="/today">{t('review.queue.openToday')}</Link><Link className="btn btn--ghost btn--sm" to="/today/observations">{t('review.queue.openObservations')}</Link></div>} /> : <ol className="review-queue__list">
        {visibleExpectations.map(expectation => <ReviewQueueItem key={expectation.id} expectation={expectation} updates={updates} onOpen={() => setReviewExpectationId(expectation.id)} />)}
      </ol>}
    </Card>

    {selectedExpectation ? <ExpectationSelfReviewForm expectation={selectedExpectation} onClose={() => setReviewExpectationId(null)} /> : null}

    <details className="review-secondary" open={comparisonOpen} onToggle={event => setComparisonOpen(event.currentTarget.open)}>
      <summary>{t('review.comparisonSecondary')}</summary>
      <p className="form-hint">{t('review.comparisonSecondaryHint')}</p>
      {comparisonOpen ? <ComparisonPanel /> : null}
    </details>

    <Card as="section" className="stack review-patterns" aria-labelledby="review-patterns-title">
      <h2 id="review-patterns-title">{t('discipline.patterns.title')}</h2>
      <div className="form-actions">{(['weekly', 'monthly', 'custom'] as const).map(value => <Button key={value} variant={range === value ? 'subtle' : 'ghost'} size="sm" onClick={() => setRange(value)}>{t(`discipline.patterns.${value}`)}</Button>)}</div>
      {range === 'custom' ? <div className="form-grid"><Field label={t('discipline.patterns.from')}><TextInput type="date" value={from} onChange={event => setFrom(event.target.value)} /></Field><Field label={t('discipline.patterns.to')}><TextInput type="date" value={to} onChange={event => setTo(event.target.value)} /></Field></div> : null}
      {patterns.isError ? <SectionError onRetry={() => { void patterns.refetch() }} /> : patterns.isLoading ? <div className="stack" role="status" aria-label={t('common.loading')}><div className="skel" style={{ height: 24, width: '65%' }} /><div className="skel" style={{ height: 18, width: '42%' }} /></div> : patterns.data ? <>
        <p>{t('discipline.patterns.reviewed', { count: patterns.data.reviewedExpectationCount })}</p>
        {Number(patterns.data.reviewedExpectationCount) === 0 ? <p className="form-hint">{t('discipline.patterns.empty')}</p> : <ol className="principle-list">{patterns.data.labels.filter(label => Number(label.count) > 0).map(label => <li key={`${label.kind}:${label.key}`}><strong>{label.name}</strong> · {label.count}/{label.denominator}<span> {label.evidence.map((evidence, index) => <a key={evidence.expectationId} className="text-link" href={evidence.url}>{t('discipline.patterns.evidence', { count: index + 1 })}</a>)}</span></li>)}</ol>}
      </> : null}
    </Card>

    <Card as="section" className="inline-form-wrap"><form className="inline-form" onSubmit={add}><TextInput value={content} onChange={event => setContent(event.target.value)} placeholder={t('discipline.placeholder')} required maxLength={280} /><Button variant="primary" type="submit" icon="plus" loading={createDiscipline.isPending}>{t('discipline.add')}</Button></form>{formError ? <p className="form-error" role="alert">{formError}</p> : null}</Card>
    {error ? <SectionError onRetry={reload} /> : loading ? <ul className="principle-list">{Array.from({ length: 3 }, (_, index) => <li key={index}><Card className="principle"><div className="skel" style={{ height: 18, width: '80%' }} /></Card></li>)}</ul> : items.length === 0 ? <EmptyBox icon="compass" title={t('discipline.emptyTitle')} hint={t('discipline.emptyHint')} /> : <ol className="principle-list">{items.map(item => <li key={item.id}><Card as="article" className="principle"><blockquote className="principle__text">{item.content}</blockquote><div className="form-actions">
      {item.status === 'active' && !item.selectedForToday ? <Button size="sm" variant="subtle" onClick={() => selectDiscipline.mutate(item.id)}>{t('discipline.select')}</Button> : null}
      {item.selectedForToday ? <Badge tone="gain">{t('discipline.selected')}</Badge> : null}
      {item.status === 'active' ? <Button size="sm" variant="ghost" onClick={() => setStatus(item, 'disabled')}>{t('discipline.disable')}</Button> : null}
      {item.status === 'disabled' ? <Button size="sm" variant="ghost" onClick={() => setStatus(item, 'active')}>{t('discipline.enable')}</Button> : null}
      {item.status !== 'archived' ? <Button size="sm" variant="danger" onClick={() => setStatus(item, 'archived')}>{t('discipline.archive')}</Button> : <Badge tone="muted">{t('discipline.archived')}</Badge>}
    </div></Card></li>)}</ol>}
  </div>
}
