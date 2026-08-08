import { useState } from 'react'
import type { FormEvent } from 'react'
import type { PatternLabel } from '../../features/api'
import { useConfirmPatternMutation, useCreateDisciplineMutation, usePatternReviewQuery, useUnconfirmPatternMutation } from '../../features/queries'
import { Badge, Button, Card, Field, TextInput } from '../../ui'
import { SectionError } from '../../shell'
import { useI18n } from '../../i18n'

function labelName(label: PatternLabel, t: ReturnType<typeof useI18n>['t']) {
  return label.system ? t(`reasoning.${label.key}` as Parameters<typeof t>[0]) : label.name
}

type PatternEvidence = PatternLabel['evidence'][number]
type TrendBucket = PatternLabel['trend']['current']

function share(bucket: TrendBucket, format: ReturnType<typeof useI18n>['format']) {
  const occurrenceCount = Number(bucket.occurrenceCount)
  const reviewedExpectationCount = Number(bucket.reviewedExpectationCount)
  const percent = reviewedExpectationCount > 0
    ? `${format.number(100 * occurrenceCount / reviewedExpectationCount, { minimumFractionDigits: 1, maximumFractionDigits: 1 })}%`
    : '—'
  return `${occurrenceCount} of ${reviewedExpectationCount} (${percent})`
}

function EvidenceList({ evidence }: { evidence: PatternEvidence[] }) {
  const { t, format } = useI18n()
  return <ol>{evidence.map(item => <li key={item.reviewId}>
    <div className="pattern-evidence__heading"><strong>{item.subject}</strong><time dateTime={item.reviewedAt}>{format.date(item.journalDay)}</time></div>
    <p>{item.expectedBehavior}</p>
    <p className="form-hint">{t('today.review.outcome')}: {t(`today.review.outcome.${item.outcome === 'partially_confirmed' ? 'partiallyConfirmed' : item.outcome}` as Parameters<typeof t>[0])} · {t('today.review.quality')}: {t(`today.review.quality.${item.reasoningQuality}` as Parameters<typeof t>[0])}</p>
    <blockquote>{item.observationExcerpt}</blockquote>
    {item.reviewExplanation ? <p>{item.reviewExplanation}</p> : null}
    <a className="text-link" href={item.url}>{t('discipline.patterns.openEvidence')}</a>
  </li>)}</ol>
}

export function PatternReviewSection() {
  const { t, format } = useI18n()
  const [range, setRange] = useState<'weekly' | 'monthly' | 'custom'>('monthly')
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [principlePatternId, setPrinciplePatternId] = useState<string | null>(null)
  const [principle, setPrinciple] = useState('')
  const [formError, setFormError] = useState('')
  const patterns = usePatternReviewQuery(range, from, to)
  const confirm = useConfirmPatternMutation()
  const unconfirm = useUnconfirmPatternMutation()
  const createDiscipline = useCreateDisciplineMutation()
  const visibleLabels = (patterns.data?.labels ?? []).filter(label => {
    const trend = label.trend
    return Number(label.count) > 0 || Boolean(label.confirmedPatternId)
      || Number(trend.current.occurrenceCount) + Number(trend.previous?.occurrenceCount ?? 0) > 0
  })

  async function addPrinciple(event: FormEvent, patternId: string) {
    event.preventDefault()
    if (!principle.trim()) return
    setFormError('')
    try {
      await createDiscipline.mutateAsync({ content: principle.trim(), confirmedPatternId: patternId })
      setPrinciple('')
      setPrinciplePatternId(null)
    } catch {
      setFormError(t('discipline.addError'))
    }
  }

  return <Card as="section" className="stack review-patterns" aria-labelledby="review-patterns-title">
    <header>
      <p className="eyebrow">{t('discipline.patterns.eyebrow')}</p>
      <h2 id="review-patterns-title">{t('discipline.patterns.title')}</h2>
      <p className="form-hint">{t('discipline.patterns.subtitle')}</p>
    </header>
    <div className="form-actions">{(['weekly', 'monthly', 'custom'] as const).map(value => <Button key={value} variant={range === value ? 'subtle' : 'ghost'} size="sm" onClick={() => setRange(value)}>{t(`discipline.patterns.${value}`)}</Button>)}</div>
    {range === 'custom' ? <div className="form-grid"><Field label={t('discipline.patterns.from')}><TextInput type="date" value={from} onChange={event => setFrom(event.target.value)} /></Field><Field label={t('discipline.patterns.to')}><TextInput type="date" value={to} onChange={event => setTo(event.target.value)} /></Field></div> : null}
    {patterns.isError ? <SectionError onRetry={() => { void patterns.refetch() }} /> : patterns.isLoading ? <div className="stack" role="status" aria-label={t('common.loading')}><div className="skel" style={{ height: 24, width: '65%' }} /><div className="skel" style={{ height: 18, width: '42%' }} /></div> : patterns.data ? <>
      <p>{t('discipline.patterns.reviewed', { count: patterns.data.reviewedExpectationCount })}</p>
      <p className="form-hint"><time dateTime={patterns.data.from}>{format.date(patterns.data.from)}</time> – <time dateTime={patterns.data.to}>{format.date(patterns.data.to)}</time></p>
      {Number(patterns.data.reviewedExpectationCount) === 0 ? <p className="form-hint">{t('discipline.patterns.empty')}</p> : null}
      {visibleLabels.length === 0 && Number(patterns.data.reviewedExpectationCount) > 0 ? <p className="form-hint">{t('discipline.patterns.noLabels')}</p> : null}
      {visibleLabels.length > 0 ? <ol className="pattern-list">{visibleLabels.map(label => {
        const name = labelName(label, t)
        const trend = label.trend
        const patternIsConfirmed = Boolean(label.confirmedPatternId) && label.patternIsConfirmed
        return <li key={`${label.kind}:${label.key}`}>
          <article className="pattern-item">
            <header className="pattern-item__header">
              <div><Badge tone={label.kind === 'issue' ? 'muted' : 'gain'}>{label.kind === 'issue' ? t('today.review.issues') : t('today.review.strengths')}</Badge><h3>{name}</h3></div>
              <strong>{label.count}/{label.denominator}</strong>
            </header>
            <p>{t('discipline.patterns.objectiveStatement', { label: name, count: label.count, denominator: label.denominator })}</p>
            <dl className="pattern-item__dates">
              <div><dt>{t('discipline.patterns.firstSeen')}</dt><dd>{label.firstSeen ? format.dateTime(label.firstSeen) : t('common.emptyValue')}</dd></div>
              <div><dt>{t('discipline.patterns.mostRecent')}</dt><dd>{label.mostRecent ? format.dateTime(label.mostRecent) : t('common.emptyValue')}</dd></div>
            </dl>
            <div className="form-actions">
              {patternIsConfirmed ? <><Badge tone="gain">{t('discipline.patterns.confirmed')}</Badge><Button size="sm" variant="ghost" loading={unconfirm.isPending} onClick={() => unconfirm.mutate(label.confirmedPatternId!)}>{t('discipline.patterns.unconfirm')}</Button></>
                : label.confirmedPatternId ? <><Badge tone="muted">{t('discipline.patterns.unconfirmed')}</Badge><Button size="sm" variant="subtle" loading={confirm.isPending} onClick={() => confirm.mutate({ kind: label.kind, key: label.key })}>{t('discipline.patterns.reconfirm')}</Button></>
                  : Number(label.count) >= 2 ? <Button size="sm" variant="subtle" loading={confirm.isPending} onClick={() => confirm.mutate({ kind: label.kind, key: label.key })}>{t('discipline.patterns.confirm')}</Button> : <span className="form-hint">{t('discipline.patterns.needsMoreEvidence')}</span>}
              {patternIsConfirmed && principlePatternId !== label.confirmedPatternId ? <Button size="sm" variant="ghost" onClick={() => { setPrinciplePatternId(label.confirmedPatternId!); setPrinciple(''); setFormError('') }}>{t('discipline.patterns.createPrinciple')}</Button> : null}
            </div>
            {label.confirmedPatternId && principlePatternId === label.confirmedPatternId ? <form className="pattern-item__principle" onSubmit={event => { void addPrinciple(event, label.confirmedPatternId!) }}>
              <Field label={t('discipline.patterns.principleLabel')} hint={t('discipline.patterns.principleHint')}><TextInput value={principle} onChange={event => setPrinciple(event.target.value)} maxLength={280} /></Field>
              {formError ? <p className="form-error" role="alert">{formError}</p> : null}
              <div className="form-actions"><Button type="submit" size="sm" variant="primary" loading={createDiscipline.isPending} disabled={!principle.trim()}>{t('discipline.add')}</Button><Button type="button" size="sm" variant="ghost" onClick={() => setPrinciplePatternId(null)}>{t('common.cancel')}</Button></div>
            </form> : null}
            <section className="stack pattern-trend" aria-label={t('discipline.patterns.trendTitle')}>
              <h4>{t('discipline.patterns.trendTitle')}</h4>
              <p>{trend.status === 'supported' && trend.previous && trend.direction
                ? t(`discipline.patterns.trend.${trend.direction}`, { current: share(trend.current, format), previous: share(trend.previous, format) })
                : t(trend.previous && Number(trend.current.reviewedExpectationCount) > 0 && Number(trend.previous.reviewedExpectationCount) > 0
                  ? 'discipline.patterns.trend.insufficient'
                  : 'discipline.patterns.trend.missing')}</p>
              <details className="pattern-evidence">
                <summary>{t('discipline.patterns.trend.current', { to: format.date(trend.current.to), share: share(trend.current, format) })}</summary>
                <EvidenceList evidence={trend.current.evidence} />
              </details>
              {trend.previous ? <details className="pattern-evidence">
                <summary>{t('discipline.patterns.trend.previous', { share: share(trend.previous, format) })}</summary>
                <EvidenceList evidence={trend.previous.evidence} />
              </details> : null}
            </section>
            <details className="pattern-evidence">
              <summary>{t('discipline.patterns.evidenceSummary', { count: label.evidence.length })}</summary>
              <EvidenceList evidence={label.evidence} />
            </details>
          </article>
        </li>
      })}</ol> : null}
    </> : null}
  </Card>
}
