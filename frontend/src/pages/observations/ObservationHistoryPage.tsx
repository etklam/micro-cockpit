import type { FormEvent } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import type { ObservationSearchFilters } from '../../features/api'
import { useObservationHistoryQuery } from '../../features/queries'
import { Button, Card, EmptyBox, Field, PageHeader, SelectBox, TextInput } from '../../ui'
import { PageSkeleton, SectionError } from '../../shell'
import { useI18n } from '../../i18n'
import { DailyCloseEvidence, subjectHistoryHref, subjectLabel } from '../shared'

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
