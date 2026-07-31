import { useState, type FormEvent } from 'react'
import type { Discipline, DisciplinePrincipleStatus } from '../../features/api'
import {
  useCreateDisciplineMutation,
  useDisciplinesQuery,
  useExpectationsQuery,
  usePatternReviewQuery,
  useSelectDisciplineMutation,
  useUpdateDisciplineMutation,
} from '../../features/queries'
import { Badge, Button, Card, EmptyBox, Field, PageHeader, TextInput } from '../../ui'
import { SectionError } from '../../shell'
import { useI18n } from '../../i18n'
import { ExpectationReviewForm } from '../today/ExpectationReviewForm'
import { ComparisonPanel } from './ComparisonPanel'

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

  return <>
    <PageHeader title={t('review.title')} subtitle={t('review.subtitle')} />
    <ComparisonPanel />
    <Card as="section" className="stack">
      <h2>{t('review.expectations')}</h2>
      {expectations.isLoading ? <div className="stack" role="status" aria-label={t('common.loading')}><div className="skel" style={{ height: 24, width: '75%' }} /><div className="skel" style={{ height: 18, width: '48%' }} /></div> : expectations.isError ? <SectionError onRetry={() => { void expectations.refetch() }} /> : reviewable.length === 0 ? <p className="form-hint">{t('review.expectationsEmpty')}</p> : <ol className="principle-list">{reviewable.map(expectation => <li key={expectation.id}>
        <article className="stack"><strong>{expectation.expectedBehavior}</strong><span>{t('today.expectations.confidence')}: {expectation.confidence}</span><span>{t('today.expectations.readiness')}: {expectation.readiness}</span><Button variant="ghost" size="sm" onClick={() => setReviewExpectationId(expectation.id)}>{expectation.readiness === 'reviewed' ? t('today.review.edit') : t('today.review.open')}</Button></article>
      </li>)}</ol>}
    </Card>
    {reviewExpectationId ? <ExpectationReviewForm expectationId={reviewExpectationId} onClose={() => setReviewExpectationId(null)} /> : null}
    <Card as="section" className="stack">
      <h2>{t('discipline.patterns.title')}</h2>
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
  </>
}
