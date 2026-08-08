import { useMemo, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import type { Expectation, ObservationSearchItem } from '../../features/api'
import {
  useExpectationReviewQuery,
  useExpectationsQuery,
  useObservationHistoryQuery,
} from '../../features/queries'
import { Badge, Button, Card, EmptyBox, PageHeader } from '../../ui'
import { SectionError } from '../../shell'
import { useI18n } from '../../i18n'
import { ExpectationSelfReviewForm } from './ExpectationSelfReviewForm'
import { ComparisonPanel } from './ComparisonPanel'
import { PatternReviewSection } from './PatternReviewSection'
import { DisciplinePrinciplesSection } from './DisciplinePrinciplesSection'
import { subjectLabel } from '../shared'
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
  return expectation.readiness === 'reviewed'
    ? t('review.status.reviewed')
    : expectation.readiness === 'ready_for_review'
      ? t('today.expectations.readiness.ready')
      : t('today.expectations.readiness.active')
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
  const observation = updates.get(expectation.observationUpdateId)
  const comparison = comparisonHref(expectation, observation)
  const relationshipHref = `/today/observations?from=${encodeURIComponent(expectation.journalDay)}&to=${encodeURIComponent(expectation.journalDay)}`

  return <li>
    <article className="review-queue__item">
      <header className="review-queue__item-header">
        <div>
          <p className="review-queue__target">{observation?.update.primarySubject ? subjectLabel(observation.update.primarySubject) : expectation.market}</p>
          <h3>{expectation.expectedBehavior}</h3>
        </div>
        <Badge tone={reviewed ? 'gain' : 'primary'}>{reviewStatus(expectation, t)}</Badge>
      </header>
      <dl className="review-queue__facts">
        <div><dt>{t('review.relationship')}</dt><dd><Link className="text-link" to={relationshipHref}>{format.date(expectation.journalDay)}</Link></dd></div>
        <div><dt>{t('today.expectations.confidence')}</dt><dd>{expectation.confidence}</dd></div>
        <div><dt>{t('today.expectations.deadline')}</dt><dd><time dateTime={expectation.deadline}>{format.dateTime(expectation.deadline)}</time></dd></div>
        <div><dt>{t('today.expectations.invalidation')}</dt><dd>{expectation.invalidationCondition}</dd></div>
        <div><dt>{t('review.currentStatus')}</dt><dd>{reviewStatus(expectation, t)}</dd></div>
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
  const [searchParams, setSearchParams] = useSearchParams()
  const [queue, setQueue] = useState<'pending' | 'completed'>('pending')
  const [comparisonOpen, setComparisonOpen] = useState(() => ['agentUserId', 'subjectType', 'subject', 'instrumentId', 'from', 'to', 'period'].some(key => searchParams.has(key)))
  const reviewExpectationId = searchParams.get('expectationId')

  const expectations = useExpectationsQuery()
  const observationHistory = useObservationHistoryQuery({})
  const expectationItems = expectations.data ?? []
  const pending = expectationItems.filter(item => item.readiness === 'ready_for_review')
  const completed = expectationItems.filter(item => item.readiness === 'reviewed')
  const visibleExpectations = queue === 'pending' ? pending : completed
  const selectedExpectation = expectationItems.find(item => item.id === reviewExpectationId)
  const updates = useMemo(() => new Map((observationHistory.data?.pages.flatMap(page => page.items) ?? []).map(item => [item.update.id, item])), [observationHistory.data?.pages])
  const setReviewExpectation = (id: string | null) => {
    const next = new URLSearchParams(searchParams)
    if (id) next.set('expectationId', id)
    else next.delete('expectationId')
    setSearchParams(next)
  }

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
        {visibleExpectations.map(expectation => <ReviewQueueItem key={expectation.id} expectation={expectation} updates={updates} onOpen={() => setReviewExpectation(expectation.id)} />)}
      </ol>}
    </Card>

    {selectedExpectation ? <ExpectationSelfReviewForm expectation={selectedExpectation} onClose={() => setReviewExpectation(null)} /> : null}

    <PatternReviewSection />
    <DisciplinePrinciplesSection />

    <details className="review-secondary" open={comparisonOpen} onToggle={event => setComparisonOpen(event.currentTarget.open)}>
      <summary>{t('review.comparisonSecondary')}</summary>
      <p className="form-hint">{t('review.comparisonSecondaryHint')}</p>
      {comparisonOpen ? <ComparisonPanel /> : null}
    </details>
  </div>
}
