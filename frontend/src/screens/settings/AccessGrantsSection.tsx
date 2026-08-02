import type { FormEvent } from 'react'
import type { AccessGrant, AgentManagement } from '../../features/api'
import { Button, Card, Field, SelectBox, TextInput } from '../../ui'
import { useI18n } from '../../i18n'

type GrantScope = 'all' | 'broad_market' | 'sector' | 'theme' | 'instrument'

type AccessGrantsSectionProps = {
  agents: AgentManagement[] | undefined
  agentsLoading: boolean
  agentsError: boolean
  grants: AccessGrant[] | undefined
  grantAgentId: string
  setGrantAgentId: (value: string) => void
  grantMode: 'fixed' | 'ongoing'
  setGrantMode: (value: 'fixed' | 'ongoing') => void
  grantFrom: string
  setGrantFrom: (value: string) => void
  grantTo: string
  setGrantTo: (value: string) => void
  grantScope: GrantScope
  setGrantScope: (value: GrantScope) => void
  grantSubject: string
  setGrantSubject: (value: string) => void
  grantExpiry: string
  setGrantExpiry: (value: string) => void
  onCreate: (event: FormEvent<HTMLFormElement>) => void
  createPending: boolean
  busyAgentId: string
  onRevoke: (id: string) => void
}

export function AccessGrantsSection({
  agents, agentsLoading, agentsError, grants, grantAgentId, setGrantAgentId, grantMode, setGrantMode, grantFrom, setGrantFrom,
  grantTo, setGrantTo, grantScope, setGrantScope, grantSubject, setGrantSubject, grantExpiry,
  setGrantExpiry, onCreate, createPending, busyAgentId, onRevoke,
}: AccessGrantsSectionProps) {
  const { t } = useI18n()
  return <section className="stack">
    <h2>{t('settings.agents.grantsTitle')}</h2>
    <p className="form-hint">{t('settings.agents.revokeWarning')}</p>
    <form className="stack" onSubmit={onCreate}>
      <Field label={t('settings.agents.grantAgent')} hint={!agentsLoading && !agentsError && !agents?.length ? t('settings.agents.noAgentsForGrantHint') : undefined}><SelectBox required disabled={agentsLoading || agentsError || !agents?.length} value={grantAgentId} onChange={event => setGrantAgentId(event.target.value)}>
        <option value="" disabled>{agentsLoading ? t('common.loading') : agents?.length ? t('settings.agents.grantAgentPlaceholder') : t('settings.agents.noAgentsForGrantOption')}</option>
        {agents?.map(agent => <option key={agent.userId} value={agent.userId}>{agent.displayName}</option>)}
      </SelectBox></Field>
      <Field label={t('settings.agents.grantMode')}><SelectBox value={grantMode} onChange={event => setGrantMode(event.target.value as 'fixed' | 'ongoing')}>
        <option value="fixed">{t('settings.agents.grantFixed')}</option><option value="ongoing">{t('settings.agents.grantOngoing')}</option>
      </SelectBox></Field>
      <div className="inline-form">
        <Field label={t('settings.agents.grantFrom')}><TextInput type="date" required value={grantFrom} onChange={event => setGrantFrom(event.target.value)} /></Field>
        <Field label={t('settings.agents.grantTo')}><TextInput type="date" required value={grantTo} onChange={event => setGrantTo(event.target.value)} /></Field>
      </div>
      <Field label={t('settings.agents.grantScope')}><SelectBox value={grantScope} onChange={event => { setGrantScope(event.target.value as GrantScope); setGrantSubject('') }}>
        <option value="all">{t('settings.agents.grantAll')}</option><option value="broad_market">{t('today.observations.subject.broadMarket')}</option><option value="sector">{t('today.observations.subject.sector')}</option><option value="theme">{t('today.observations.subject.theme')}</option><option value="instrument">{t('today.observations.subject.instrument')}</option>
      </SelectBox></Field>
      {grantScope !== 'all' ? <Field label={grantScope === 'instrument' ? t('settings.agents.grantInstrument') : t('settings.agents.grantSubject')}><TextInput required value={grantSubject} onChange={event => setGrantSubject(event.target.value)} /></Field> : null}
      <Field label={t('settings.agents.grantExpiry')} hint={t('settings.agents.grantExpiryHint')}><TextInput type="datetime-local" value={grantExpiry} onChange={event => setGrantExpiry(event.target.value)} /></Field>
      <div className="form-actions"><Button variant="primary" type="submit" disabled={agentsError || !agents?.length} loading={createPending}>{t('settings.agents.grantCreate')}</Button></div>
    </form>
    {grants?.length === 0 ? <p className="form-hint">{t('settings.agents.grantsEmpty')}</p> : null}
    {grants?.map(grant => {
      const agent = agents?.find(item => item.userId === grant.agentUserId)
      return <Card key={grant.id} className="stack">
        <h3>{agent?.displayName ?? grant.agentUserId}</h3>
        <p>{grant.mode === 'fixed' ? t('settings.agents.grantFixed') : t('settings.agents.grantOngoing')} · {grant.from} — {grant.to}</p>
        <p className="form-hint">{grant.revokedAt ? t('settings.agents.grantRevoked') : t('settings.agents.grantActive')}</p>
        {!grant.revokedAt ? <Button size="sm" variant="ghost" onClick={() => onRevoke(grant.id)} loading={busyAgentId === grant.id}>{t('settings.agents.grantRevoke')}</Button> : null}
      </Card>
    })}
  </section>
}
