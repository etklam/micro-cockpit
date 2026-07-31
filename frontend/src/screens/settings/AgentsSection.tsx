import type { FormEvent } from 'react'
import type { AgentManagement } from '../../features/api'
import { Button, Card, TextInput } from '../../ui'
import { SectionError } from '../../shell'
import { useI18n } from '../../i18n'

type AgentsSectionProps = {
  agents: AgentManagement[] | undefined
  loading: boolean
  error: boolean
  onRetry: () => void
  agentName: string
  setAgentName: (value: string) => void
  onCreate: (event: FormEvent<HTMLFormElement>) => void
  createPending: boolean
  agentToken: string
  clearAgentToken: () => void
  agentError: string
  busyAgentId: string
  rotatePending: (id: string) => boolean
  onRotate: (id: string) => void
  onRevoke: (id: string) => void
  formatAgentTime: (value: string | null) => string
}

export function AgentsSection({
  agents, loading, error, onRetry, agentName, setAgentName, onCreate, createPending, agentToken,
  clearAgentToken, agentError, busyAgentId, rotatePending, onRotate, onRevoke, formatAgentTime,
}: AgentsSectionProps) {
  const { t } = useI18n()
  return <section className="stack">
    <h2>{t('settings.agents.title')}</h2>
    <p className="form-hint">{t('settings.agents.hint')}</p>
    <form className="inline-form" onSubmit={onCreate}>
      <TextInput aria-label={t('settings.agents.name')} required maxLength={100} value={agentName} onChange={event => setAgentName(event.target.value)} placeholder={t('settings.agents.name')} disabled={createPending} />
      <Button variant="primary" type="submit" loading={createPending}>{t('settings.agents.create')}</Button>
    </form>
    {agentToken ? <div className="secret-once" role="status"><code>{agentToken}</code><Button size="sm" variant="ghost" onClick={clearAgentToken}>{t('settings.agents.saved')}</Button></div> : null}
    {agentError ? <p className="form-error" role="alert">{agentError}</p> : null}
    {loading ? <div className="stack" role="status" aria-label={t('common.loading')}><div className="skel" style={{ height: 18, width: '60%' }} /></div> : null}
    {error ? <SectionError onRetry={onRetry} /> : null}
    {!loading && !error && agents?.length === 0 ? <p className="form-hint">{t('settings.agents.empty')}</p> : null}
    {agents?.map(agent => <Card key={agent.userId} className="stack">
      <h3>{agent.displayName}</h3>
      <p className="form-hint">{agent.scopes.join(', ')}</p>
      <dl className="detail-list">
        <div><dt>{t('settings.agents.created')}</dt><dd>{formatAgentTime(agent.tokenCreatedAt)}</dd></div>
        <div><dt>{t('settings.agents.lastUsed')}</dt><dd>{formatAgentTime(agent.lastUsedAt)}</dd></div>
        <div><dt>{t('settings.agents.lastSuccess')}</dt><dd>{formatAgentTime(agent.lastSuccessfulRequestAt)}</dd></div>
      </dl>
      <div className="form-actions">
        <Button size="sm" onClick={() => onRotate(agent.userId)} loading={rotatePending(agent.userId)}>{t('settings.agents.rotate')}</Button>
        <Button size="sm" variant="ghost" onClick={() => onRevoke(agent.userId)} disabled={!!busyAgentId || !agent.keyId}>{t('settings.agents.revoke')}</Button>
      </div>
    </Card>)}
  </section>
}
