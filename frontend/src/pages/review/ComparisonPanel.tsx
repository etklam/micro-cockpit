import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import type { ComparisonQuery, OwnerComparison } from '../../features/api'
import { useAgentsQuery, useComparisonQuery, useInstrumentDirectoryQuery } from '../../features/queries'
import { Badge, Button, Card, Field, SelectBox, TextInput } from '../../ui'
import { SectionError } from '../../shell'
import { useI18n } from '../../i18n'
import { DailyCloseEvidence, subjectLabel } from '../shared'

type ComparisonSubjectType = 'theme' | 'sector' | 'broad_market' | 'instrument'
type ComparisonPeriod = '7d' | '30d' | 'custom'

const subjectTypes: ComparisonSubjectType[] = ['theme', 'sector', 'broad_market', 'instrument']

function isSubjectType(value: string | null): value is ComparisonSubjectType {
  return !!value && subjectTypes.includes(value as ComparisonSubjectType)
}

function isoDate(date: Date) {
  return date.toISOString().slice(0, 10)
}

function subtractDays(date: string, days: number) {
  const value = new Date(`${date}T00:00:00Z`)
  value.setUTCDate(value.getUTCDate() - days)
  return isoDate(value)
}

function periodDates(period: Exclude<ComparisonPeriod, 'custom'>, today: string) {
  return period === '7d'
    ? { from: subtractDays(today, 6), to: today }
    : { from: subtractDays(today, 29), to: today }
}

function subjectTypeLabel(type: ComparisonSubjectType, t: ReturnType<typeof useI18n>['t']) {
  if (type === 'theme') return t('today.observations.subject.theme')
  if (type === 'sector') return t('today.observations.subject.sector')
  if (type === 'broad_market') return t('today.observations.subject.broadMarket')
  return t('today.observations.subject.instrument')
}

export function ComparisonPanel() {
  const { t } = useI18n()
  const [searchParams, setSearchParams] = useSearchParams()
  const agents = useAgentsQuery()
  const instruments = useInstrumentDirectoryQuery()
  const today = new Date().toISOString().slice(0, 10)
  const requestedPeriod = searchParams.get('period')
  const initialPeriod = requestedPeriod === '7d' || requestedPeriod === '30d' || requestedPeriod === 'custom'
    ? requestedPeriod as ComparisonPeriod
    : searchParams.has('from') || searchParams.has('to') ? 'custom' : '7d'
  const initialDates = initialPeriod === 'custom'
    ? { from: searchParams.get('from') ?? subtractDays(today, 6), to: searchParams.get('to') ?? today }
    : periodDates(initialPeriod, today)
  const [agentUserId, setAgentUserId] = useState(searchParams.get('agentUserId') ?? '')
  const initialSubjectType = searchParams.get('subjectType') ?? (searchParams.has('instrumentId') ? 'instrument' : null)
  const [subjectType, setSubjectType] = useState<ComparisonSubjectType>(isSubjectType(initialSubjectType) ? initialSubjectType : 'theme')
  const [subject, setSubject] = useState(searchParams.get('subject') ?? '')
  const [instrumentId, setInstrumentId] = useState(searchParams.get('instrumentId') ?? '')
  const [instrumentSearch, setInstrumentSearch] = useState('')
  const [period, setPeriod] = useState<ComparisonPeriod>(initialPeriod)
  const [from, setFrom] = useState(initialDates.from)
  const [to, setTo] = useState(initialDates.to)
  const [query, setQuery] = useState<ComparisonQuery | null>(null)
  const [formError, setFormError] = useState('')
  const targetRef = useRef<HTMLInputElement | HTMLSelectElement | null>(null)
  const comparison = useComparisonQuery(query)
  const agentItems = agents.data?.items ?? []
  const instrumentItems = instruments.data ?? []
  const selectedInstrument = instrumentItems.find(item => item.instrumentId === instrumentId)
  const returnToReview = `/review${searchParams.toString() ? `?${searchParams.toString()}` : ''}`
  const settingsHref = `/settings?returnTo=${encodeURIComponent(returnToReview)}#agents`

  useEffect(() => {
    if (selectedInstrument) setInstrumentSearch(`${selectedInstrument.symbol} · ${selectedInstrument.name}`)
  }, [selectedInstrument])

  const daySpan = from && to ? (new Date(`${to}T00:00:00Z`).getTime() - new Date(`${from}T00:00:00Z`).getTime()) / 86_400_000 : 0
  const tooLong = daySpan > 366
  const valid = !!agentUserId && !!from && !!to && from <= to && !tooLong && (subjectType === 'instrument' ? !!instrumentId : !!subject.trim())
  const dateError = from && to && from > to
    ? t('comparison.invalidRange')
    : tooLong ? t('comparison.rangeTooLong') : ''
  const agentName = agentItems.find(agent => agent.userId === query?.agentUserId)?.displayName ?? t('comparison.agent')
  const targetName = query
    ? query.instrumentId
      ? instrumentItems.find(item => item.instrumentId === query.instrumentId)?.symbol ?? query.instrumentId
      : query.subject ?? t('comparison.unavailable')
    : ''

  function selectPeriod(next: ComparisonPeriod) {
    setPeriod(next)
    setFormError('')
    if (next === 'custom') return
    const dates = periodDates(next, today)
    setFrom(dates.from)
    setTo(dates.to)
  }

  function submit(event: FormEvent) {
    event.preventDefault()
    setFormError('')
    if (!valid) {
      return
    }
    const nextQuery: ComparisonQuery = subjectType === 'instrument'
      ? { agentUserId, from, to, instrumentId }
      : { agentUserId, from, to, subjectType, subject: subject.trim() }
    setQuery(nextQuery)
    const params: Record<string, string> = { agentUserId, from, to, period }
    if (subjectType === 'instrument') params.instrumentId = instrumentId
    else { params.subjectType = subjectType; params.subject = subject.trim() }
    setSearchParams(params)
  }

  function resetResult() {
    setQuery(null)
    setFormError('')
    window.setTimeout(() => targetRef.current?.focus(), 0)
  }

  return <Card as="section" className="stack comparison-panel" aria-labelledby="comparison-title">
    <div className="comparison-intro">
      <p className="eyebrow">{t('comparison.eyebrow')}</p>
      <h2 id="comparison-title">{t('comparison.title')}</h2>
      <p className="form-hint">{t('comparison.subtitle')}</p>
      <p className="form-hint comparison-legacy-hint">{t('comparison.setupHint')}</p>
    </div>
    <form onSubmit={submit} noValidate>
      <div className="comparison-steps">
        <fieldset className="comparison-setup comparison-step">
          <legend><span className="comparison-step__number">1</span>{t('comparison.stepTarget')}</legend>
          <p className="form-hint">{t('comparison.stepTargetHint')}</p>
          <div className="comparison-filters">
            <Field label={t('comparison.subjectType')}>
              <SelectBox value={subjectType} onChange={event => {
                setSubjectType(event.target.value as ComparisonSubjectType)
                setInstrumentId('')
                setInstrumentSearch('')
                setFormError('')
              }}>
                <option value="theme">{t('today.observations.subject.theme')}</option>
                <option value="sector">{t('today.observations.subject.sector')}</option>
                <option value="broad_market">{t('today.observations.subject.broadMarket')}</option>
                <option value="instrument">{t('today.observations.subject.instrument')}</option>
              </SelectBox>
            </Field>
            {subjectType === 'instrument' ? <Field label={t('comparison.instrument')} hint={instrumentItems.length ? t('comparison.targetSearchHint') : t('comparison.noInstrumentsHint')}>
              <TextInput
                ref={element => { targetRef.current = element }}
                aria-label={t('comparison.instrument')}
                value={instrumentSearch}
                onChange={event => {
                  const value = event.target.value
                  setInstrumentSearch(value)
                  const match = instrumentItems.find(item => `${item.symbol} · ${item.name}`.toLowerCase() === value.trim().toLowerCase() || item.symbol.toLowerCase() === value.trim().toLowerCase())
                  setInstrumentId(match?.instrumentId ?? '')
                }}
                list="comparison-instruments"
                placeholder={t('comparison.chooseInstrument')}
                disabled={instruments.isLoading || instruments.isError || instrumentItems.length === 0}
                required
              />
              <datalist id="comparison-instruments">{instrumentItems.map(item => <option key={item.instrumentId} value={`${item.symbol} · ${item.name}`} />)}</datalist>
            </Field> : <Field label={t('comparison.subject')} hint={t('comparison.targetSearchHint')}>
              <TextInput aria-label={t('comparison.subject')} ref={element => { targetRef.current = element }} value={subject} onChange={event => { setSubject(event.target.value); setFormError('') }} maxLength={120} required list="comparison-subjects" />
              <datalist id="comparison-subjects" />
            </Field>}
          </div>
        </fieldset>
        <fieldset className="comparison-setup comparison-step">
          <legend><span className="comparison-step__number">2</span>{t('comparison.stepPeriod')}</legend>
          <p className="form-hint">{t('comparison.stepPeriodHint')}</p>
          <div className="comparison-periods" role="group" aria-label={t('comparison.period')}>
            {(['7d', '30d', 'custom'] as const).map(value => <Button key={value} type="button" size="sm" variant={period === value ? 'subtle' : 'ghost'} aria-pressed={period === value} onClick={() => selectPeriod(value)}>
              {value === '7d' ? t('comparison.last7Days') : value === '30d' ? t('comparison.last30Days') : t('comparison.customPeriod')}
            </Button>)}
          </div>
          <div className="comparison-dates">
            <Field label={t('comparison.from')}><TextInput type="date" value={from} onChange={event => { setPeriod('custom'); setFrom(event.target.value); setFormError('') }} required aria-invalid={!!dateError} /></Field>
            <Field label={t('comparison.to')}><TextInput type="date" value={to} onChange={event => { setPeriod('custom'); setTo(event.target.value); setFormError('') }} required aria-invalid={!!dateError} /></Field>
          </div>
          {dateError ? <p className="form-error" role="alert">{dateError}</p> : null}
        </fieldset>
        <fieldset className="comparison-setup comparison-step">
          <legend><span className="comparison-step__number">3</span>{t('comparison.stepAgent')}</legend>
          <p className="form-hint">{t('comparison.stepAgentHint')}</p>
          {agentItems.length > 0 ? <Field label={t('comparison.agent')}>
            <SelectBox value={agentUserId} ref={element => { if (!targetRef.current) targetRef.current = element }} disabled={agents.isLoading || agents.isError} onChange={event => { setAgentUserId(event.target.value); setFormError('') }} required>
              <option value="">{t('comparison.chooseAgent')}</option>
              {agentItems.map(agent => <option key={agent.userId} value={agent.userId}>{agent.displayName}</option>)}
            </SelectBox>
          </Field> : agents.isLoading ? <p role="status" className="form-hint">{t('common.loading')}</p> : agents.isError ? <SectionError onRetry={() => { void agents.refetch() }} /> : <div className="comparison-agent-empty">
            <h3>{t('comparison.noAgentsTitle')}</h3>
            <p className="form-hint">{t('comparison.noAgentsHint')}</p>
            <Link className="btn btn--primary" to={settingsHref}>{t('comparison.createAgent')}</Link>
          </div>}
        </fieldset>
      </div>
      {formError ? <p className="form-error" role="alert">{formError}</p> : null}
      {agents.isError ? <p role="alert">{t('comparison.agentsUnavailable')}</p> : null}
      <div className="comparison-submit-row">
        <Button variant="primary" type="submit" disabled={!valid || agents.isLoading || agents.isError || agentItems.length === 0}>
          {t('comparison.create')}
        </Button>
      </div>
    </form>
    {!query ? <div className="comparison-pre-submit" role="status"><strong>{t('comparison.emptyTitle')}</strong><span>{t('comparison.emptyHint')}</span></div> : null}
    {query && comparison.isLoading ? <div className="comparison-loading" role="status" aria-label={t('common.loading')}><div className="skel" style={{ height: 64 }} /><span>{t('comparison.loading')}</span></div> : null}
    {comparison.isError ? <SectionError onRetry={() => { void comparison.refetch() }} /> : null}
    {comparison.data ? <ComparisonResult comparison={comparison.data} query={query!} agentName={agentName} targetName={targetName} onChangeScope={resetResult} /> : null}
  </Card>
}

function ComparisonResult({ comparison, query, agentName, targetName, onChangeScope }: { comparison: OwnerComparison; query: ComparisonQuery; agentName: string; targetName: string; onChangeScope: () => void }) {
  const { t } = useI18n()
  const outcome = comparison.difference.outcomeConsistent == null ? null : comparison.difference.outcomeConsistent ? t('comparison.same') : t('comparison.different')
  const confidence = comparison.difference.confidenceDifference == null
    ? null
    : Number(comparison.difference.confidenceDifference) === 0
      ? t('comparison.same')
      : t('comparison.confidenceDifference', { value: Number(comparison.difference.confidenceDifference) > 0 ? `+${comparison.difference.confidenceDifference}` : comparison.difference.confidenceDifference })
  return <section className="comparison-result stack" aria-labelledby="comparison-result-title">
    <header className="comparison-result__header">
      <div>
        <p className="eyebrow">{t('comparison.resultEyebrow')}</p>
        <h3 id="comparison-result-title">{t('comparison.resultTitle')}</h3>
        <p className="form-hint">{subjectTypeLabel((query.subjectType as ComparisonSubjectType | undefined) ?? 'instrument', t)} · {targetName} · {query.from} – {query.to} · {t('comparison.stepAgent')}: {agentName}</p>
      </div>
      <Button type="button" variant="ghost" size="sm" onClick={onChangeScope}>{t('comparison.changeScope')}</Button>
    </header>
    <p className="comparison-readonly-note">{t('comparison.resultHint')}</p>
    <dl className="comparison-differences" aria-label={t('comparison.objectiveDifferences')}>
      {outcome !== null ? <div><dt>{t('comparison.outcomeConsistency')}</dt><dd>{outcome}</dd></div> : null}
      {confidence !== null ? <div><dt>{t('comparison.confidence')}</dt><dd>{confidence}</dd></div> : null}
      {outcome === null && confidence === null ? <div><dt>{t('comparison.objectiveDifferences')}</dt><dd>{t('comparison.unavailable')}</dd></div> : null}
    </dl>
    <div className="comparison-columns"><ComparisonOwner title={t('comparison.human')} owner={comparison.human} /><ComparisonOwner title={agentName} owner={comparison.agent} /></div>
  </section>
}

function ComparisonOwner({ title, owner }: { title: string; owner: OwnerComparison['human'] }) {
  const { t, format } = useI18n()
  const ownerLabel = owner.ownerType === 'human' ? t('comparison.owner.human') : t('comparison.owner.agent')
  return <section className="comparison-owner" aria-label={`${title} · ${ownerLabel}`} aria-readonly="true">
    <header className="comparison-owner__header"><div><h3>{title}</h3><p className="comparison-owner__label">{ownerLabel}</p></div><Badge tone={owner.availability === 'available' ? 'gain' : 'muted'}>{owner.availability === 'unavailable' ? t('comparison.grantUnavailableShort') : owner.availability === 'empty' ? t('comparison.emptyShort') : t('comparison.available')}</Badge></header>
    {owner.availability === 'unavailable' ? <p className="form-hint">{t('comparison.grantUnavailable')}</p> : owner.observations.length === 0 ? <p className="form-hint">{t('comparison.empty')}</p> : <ol className="comparison-records">
      {owner.observations.map(observation => <li key={observation.update.id}><article className="comparison-record stack">
        <header className="comparison-record__header"><time dateTime={observation.update.recordedAt}>{format.dateTime(observation.update.recordedAt)}</time><span>{t('comparison.journalDay', { date: observation.journalDay })}</span></header>
        <p className="comparison-record__content">{observation.update.content}</p>
        {observation.update.primarySubject ? <p className="comparison-record__subject"><span>{subjectLabel(observation.update.primarySubject)}</span><DailyCloseEvidence subject={observation.update.primarySubject} /></p> : null}
        {observation.update.evidence ? <p className="comparison-record__evidence"><a className="text-link" href={observation.update.evidence.url} target="_blank" rel="noreferrer">{observation.update.evidence.title ?? t('comparison.openEvidence')}</a></p> : null}
        {observation.expectations.length === 0 ? <p className="form-hint">{t('comparison.expectationUnavailable')}</p> : observation.expectations.map(expectation => <dl key={expectation.id} className="comparison-expectation">
          <div><dt>{t('today.expectations.expectedBehavior')}</dt><dd>{expectation.expectedBehavior}</dd></div><div><dt>{t('today.expectations.confidence')}</dt><dd>{expectation.confidence}</dd></div><div><dt>{t('today.review.outcome')}</dt><dd>{expectation.outcome ?? t('comparison.unavailable')}</dd></div><div><dt>{t('today.review.quality')}</dt><dd>{expectation.reasoningQuality ?? t('comparison.unavailable')}</dd></div>
        </dl>)}
      </article></li>)}
    </ol>}
  </section>
}
