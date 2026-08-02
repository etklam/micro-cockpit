import { useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import type { ComparisonQuery, OwnerComparison } from '../../features/api'
import { useAgentsQuery, useComparisonQuery, useInstrumentDirectoryQuery } from '../../features/queries'
import { Button, Card, EmptyBox, Field, SelectBox, TextInput } from '../../ui'
import { SectionError } from '../../shell'
import { useI18n } from '../../i18n'
import { DailyCloseEvidence, subjectLabel } from '../shared'

export function ComparisonPanel() {
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
  const agentItems = agents.data?.items ?? []
  const instrumentItems = instruments.data ?? []
  const valid = !!agentUserId && !!from && !!to && from <= to && (subjectType === 'instrument' ? !!instrumentId : !!subject.trim())

  function submit(event: FormEvent) {
    event.preventDefault()
    if (!valid) return
    setQuery(subjectType === 'instrument'
      ? { agentUserId, from, to, instrumentId }
      : { agentUserId, from, to, subjectType, subject: subject.trim() })
  }

  return <Card as="section" className="stack" aria-labelledby="comparison-title">
    <div><h2 id="comparison-title">{t('comparison.title')}</h2><p className="form-hint">{t('comparison.subtitle')}</p></div>
    <form onSubmit={submit}>
      <fieldset className="comparison-setup">
        <legend>{t('comparison.setupTitle')}</legend>
        <p className="form-hint">{t('comparison.setupHint')}</p>
        <div className="comparison-filters">
          <div className="stack"><Field label={t('comparison.agent')}><SelectBox value={agentUserId} disabled={agents.isLoading || agents.isError || agentItems.length === 0} onChange={event => setAgentUserId(event.target.value)} required>
            <option value="">{agents.isLoading ? t('common.loading') : agentItems.length ? t('comparison.chooseAgent') : t('comparison.noAgentsOption')}</option>
            {agentItems.map(agent => <option key={agent.userId} value={agent.userId}>{agent.displayName}</option>)}
          </SelectBox></Field>{!agents.isLoading && !agents.isError && agentItems.length === 0 ? <p className="form-hint">{t('comparison.noAgentsHint')} <Link className="text-link" to="/settings">{t('comparison.openSettings')}</Link></p> : null}</div>
          <Field label={t('comparison.subjectType')}><SelectBox value={subjectType} onChange={event => setSubjectType(event.target.value as typeof subjectType)}>
            <option value="theme">{t('today.observations.subject.theme')}</option><option value="sector">{t('today.observations.subject.sector')}</option><option value="broad_market">{t('today.observations.subject.broadMarket')}</option><option value="instrument">{t('today.observations.subject.instrument')}</option>
          </SelectBox></Field>
          {subjectType === 'instrument' ? <Field label={t('comparison.instrument')} hint={!instruments.isLoading && !instruments.isError && instrumentItems.length === 0 ? t('comparison.noInstrumentsHint') : undefined}><SelectBox value={instrumentId} disabled={instruments.isLoading || instruments.isError || instrumentItems.length === 0} onChange={event => setInstrumentId(event.target.value)} required>
            <option value="">{instruments.isLoading ? t('common.loading') : instrumentItems.length ? t('comparison.chooseInstrument') : t('comparison.noInstrumentsOption')}</option>
            {instrumentItems.map(instrument => <option key={instrument.instrumentId} value={instrument.instrumentId}>{instrument.symbol} · {instrument.name}</option>)}</SelectBox></Field> : <Field label={t('comparison.subject')}><TextInput value={subject} onChange={event => setSubject(event.target.value)} required maxLength={120} /></Field>}
          <Field label={t('comparison.from')}><TextInput type="date" value={from} onChange={event => setFrom(event.target.value)} required /></Field>
          <Field label={t('comparison.to')}><TextInput type="date" value={to} onChange={event => setTo(event.target.value)} required /></Field>
          <Button variant="primary" type="submit" disabled={!valid}>{t('comparison.open')}</Button>
        </div>
      </fieldset>
    </form>
    {agents.isError ? <p role="alert">{t('comparison.agentsUnavailable')}</p> : null}
    {query && comparison.isLoading ? <div className="skel" style={{ height: 64, marginTop: 16 }} role="status" aria-label={t('common.loading')} /> : null}
    {comparison.isError ? <SectionError onRetry={() => { void comparison.refetch() }} /> : null}
    {!query ? <EmptyBox dense icon="compass" title={t('comparison.emptyTitle')} hint={t('comparison.emptyHint')} /> : null}
    {comparison.data ? <ComparisonResult comparison={comparison.data} agentName={agentName} /> : null}
  </Card>
}

function ComparisonResult({ comparison, agentName }: { comparison: OwnerComparison; agentName: string }) {
  const { t } = useI18n()
  const outcome = comparison.difference.outcomeConsistent == null ? t('comparison.unavailable') : comparison.difference.outcomeConsistent ? t('comparison.same') : t('comparison.different')
  const confidence = comparison.difference.confidenceDifference == null
    ? t('comparison.unavailable')
    : Number(comparison.difference.confidenceDifference) === 0
      ? t('comparison.same')
      : t('comparison.confidenceDifference', { value: Number(comparison.difference.confidenceDifference) > 0 ? `+${comparison.difference.confidenceDifference}` : comparison.difference.confidenceDifference })
  return <section className="comparison-result stack" aria-labelledby="comparison-result-title">
    <div><h3 id="comparison-result-title">{t('comparison.resultTitle')}</h3><p className="form-hint">{t('comparison.resultHint')}</p></div>
    <dl className="comparison-differences" aria-label={t('comparison.objectiveDifferences')}>
      <div><dt>{t('comparison.outcomeConsistency')}</dt><dd>{outcome}</dd></div><div><dt>{t('comparison.confidence')}</dt><dd>{confidence}</dd></div>
    </dl>
    <div className="comparison-columns"><ComparisonOwner title={t('comparison.human')} owner={comparison.human} /><ComparisonOwner title={agentName} owner={comparison.agent} /></div>
  </section>
}

function ComparisonOwner({ title, owner }: { title: string; owner: OwnerComparison['human'] }) {
  const { t } = useI18n()
  return <section className="comparison-owner" aria-label={`${title} · ${t(`comparison.owner.${owner.ownerType}`)}`}>
    <h3>{title}</h3><p className="comparison-owner__label">{t(`comparison.owner.${owner.ownerType}`)}</p>
    {owner.availability === 'unavailable' ? <p className="form-hint">{t('comparison.grantUnavailable')}</p> : owner.observations.length === 0 ? <p className="form-hint">{t('comparison.empty')}</p> : <ol className="comparison-records">
      {owner.observations.map(observation => <li key={observation.update.id}><article className="stack">
        <time dateTime={observation.update.recordedAt}>{observation.journalDay}</time><p>{observation.update.content}</p>
        {observation.update.primarySubject ? <p>{subjectLabel(observation.update.primarySubject)}<DailyCloseEvidence subject={observation.update.primarySubject} /></p> : null}
        {observation.expectations.length === 0 ? <p className="form-hint">{t('comparison.expectationUnavailable')}</p> : observation.expectations.map(expectation => <dl key={expectation.id} className="comparison-expectation">
          <div><dt>{t('today.expectations.expectedBehavior')}</dt><dd>{expectation.expectedBehavior}</dd></div><div><dt>{t('today.expectations.confidence')}</dt><dd>{expectation.confidence}</dd></div><div><dt>{t('today.review.outcome')}</dt><dd>{expectation.outcome ?? t('comparison.unavailable')}</dd></div><div><dt>{t('today.review.quality')}</dt><dd>{expectation.reasoningQuality ?? t('comparison.unavailable')}</dd></div>
        </dl>)}
      </article></li>)}
    </ol>}
  </section>
}
