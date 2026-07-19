import { useMemo, useState, type FormEvent } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import type { PartnerCompare, PartnerInvitation, PartnerLink } from '../features/api'
import {
  useAcceptPartnerMutation,
  useCreateAgentMutation,
  useCreatePartnerInvitationMutation,
  usePartnerCompareQuery,
  usePartnerInvitationsQuery,
  usePartnerShareDiariesMutation,
  usePartnersQuery,
  useRedeemPartnerInvitationMutation,
  useRevokePartnerInvitationMutation,
  useRevokePartnerMutation,
} from '../features/queries'
import { MarkdownView } from '../features/markdown'
import { useI18n } from '../i18n'
import { Badge, Button, Card, EmptyBox, Field, PageHeader, TextInput, useConfirm } from '../ui'
import { PageSkeleton, SectionError } from '../shell'

function formatDate(value: string, locale: string) {
  try { return new Date(value).toLocaleString(locale) } catch { return value }
}

function formatDay(value: string, locale: string) {
  try { return new Date(`${value}T00:00:00`).toLocaleDateString(locale) } catch { return value }
}

function defaultRange() {
  const to = new Date()
  const from = new Date()
  from.setUTCDate(from.getUTCDate() - 29)
  const iso = (d: Date) => d.toISOString().slice(0, 10)
  return { from: iso(from), to: iso(to) }
}

export function PartnersPage() {
  const { t, locale } = useI18n()
  const { confirm, confirmNode } = useConfirm()
  const partners = usePartnersQuery()
  const invitations = usePartnerInvitationsQuery()
  const createInvite = useCreatePartnerInvitationMutation()
  const revokeInvite = useRevokePartnerInvitationMutation()
  const redeem = useRedeemPartnerInvitationMutation()
  const accept = useAcceptPartnerMutation()
  const revoke = useRevokePartnerMutation()
  const share = usePartnerShareDiariesMutation()
  const createAgent = useCreateAgentMutation()

  const [rawCode, setRawCode] = useState<string | null>(null)
  const [redeemCode, setRedeemCode] = useState('')
  const [agentName, setAgentName] = useState('')
  const [agentKey, setAgentKey] = useState('')
  const [error, setError] = useState('')
  const [busyId, setBusyId] = useState<string | null>(null)

  const items = partners.data?.items ?? []
  const invites = invitations.data?.items ?? []
  const accepted = items.filter(x => x.status === 'accepted')
  const pending = items.filter(x => x.status === 'pending')

  async function onCreateInvite() {
    setError('')
    try {
      const created = await createInvite.mutateAsync()
      setRawCode(created.code)
    } catch {
      setError(t('partners.error.createInvite'))
    }
  }

  async function onRedeem(e: FormEvent) {
    e.preventDefault()
    setError('')
    try {
      await redeem.mutateAsync(redeemCode.trim())
      setRedeemCode('')
    } catch {
      setError(t('partners.error.redeem'))
    }
  }

  async function onRevokeInvite(item: PartnerInvitation) {
    const ok = await confirm({
      title: t('partners.invite.revokeTitle'),
      message: t('partners.invite.revokeMessage'),
      confirmText: t('partners.invite.revoke'),
      tone: 'danger',
    })
    if (!ok) return
    setBusyId(item.id)
    try { await revokeInvite.mutateAsync(item.id) }
    catch { setError(t('partners.error.revokeInvite')) }
    finally { setBusyId(null) }
  }

  async function onAccept(link: PartnerLink) {
    setBusyId(link.id)
    setError('')
    try { await accept.mutateAsync(link.id) }
    catch { setError(t('partners.error.accept')) }
    finally { setBusyId(null) }
  }

  async function onRevoke(link: PartnerLink) {
    const ok = await confirm({
      title: t('partners.revokeTitle'),
      message: t('partners.revokeMessage', { name: link.partnerDisplayName }),
      confirmText: t('partners.revoke'),
      tone: 'danger',
    })
    if (!ok) return
    setBusyId(link.id)
    try { await revoke.mutateAsync(link.id) }
    catch { setError(t('partners.error.revoke')) }
    finally { setBusyId(null) }
  }

  async function onToggleShare(link: PartnerLink, next: boolean) {
    setBusyId(link.id)
    setError('')
    try { await share.mutateAsync({ id: link.id, shareDiaries: next }) }
    catch { setError(t('partners.error.share')) }
    finally { setBusyId(null) }
  }

  async function onCreateAgent(e: FormEvent) {
    e.preventDefault()
    setError('')
    setAgentKey('')
    try {
      const x = await createAgent.mutateAsync(agentName.trim())
      setAgentKey(x.apiKey)
      setAgentName('')
    } catch {
      setError(t('partners.error.createAgent'))
    }
  }

  const loading = partners.isLoading || invitations.isLoading
  if (loading) return <PageSkeleton rows={3} />
  if (partners.isError) return <SectionError onRetry={() => { void partners.refetch() }} />

  return (
    <>
      {confirmNode}
      <PageHeader title={t('partners.title')} subtitle={t('partners.subtitle')} />
      {error ? <p className="form-error" role="alert">{error}</p> : null}

      <Card className="stack">
        <h2>{t('partners.invite.createTitle')}</h2>
        <p className="is-muted">{t('partners.invite.createHint')}</p>
        <div className="inline-form">
          <Button variant="primary" onClick={() => { void onCreateInvite() }} loading={createInvite.isPending}>
            {t('partners.invite.create')}
          </Button>
        </div>
        {rawCode ? (
          <div className="secret-once" role="status">
            <code>{rawCode}</code>
            <Button size="sm" onClick={() => { void navigator.clipboard.writeText(rawCode) }}>{t('partners.invite.copy')}</Button>
            <Button size="sm" variant="ghost" onClick={() => setRawCode(null)}>{t('partners.invite.saved')}</Button>
          </div>
        ) : null}
      </Card>

      <Card className="stack">
        <h2>{t('partners.invite.redeemTitle')}</h2>
        <form className="inline-form" onSubmit={onRedeem}>
          <TextInput
            aria-label={t('partners.invite.codeLabel')}
            required
            value={redeemCode}
            onChange={e => setRedeemCode(e.target.value)}
            placeholder={t('partners.invite.codePlaceholder')}
            autoComplete="off"
          />
          <Button variant="primary" type="submit" loading={redeem.isPending}>{t('partners.invite.redeem')}</Button>
        </form>
      </Card>

      <section className="stack">
        <h2>{t('partners.invite.openTitle')}</h2>
        {invites.length === 0 ? (
          <EmptyBox title={t('partners.invite.openEmpty')} hint={t('partners.invite.openEmptyHint')} />
        ) : (
          <div className="card-grid">
            {invites.map(item => (
              <Card key={item.id} className="stack">
                <div><Badge tone="muted">{t('partners.status.pending')}</Badge></div>
                <p className="is-muted">{t('partners.invite.expires', { when: formatDate(item.expiresAt, locale) })}</p>
                <Button size="sm" variant="ghost" loading={busyId === item.id} onClick={() => { void onRevokeInvite(item) }}>
                  {t('partners.invite.revoke')}
                </Button>
              </Card>
            ))}
          </div>
        )}
      </section>

      <section className="stack">
        <h2>{t('partners.acceptedTitle')}</h2>
        {accepted.length === 0 ? (
          <EmptyBox title={t('partners.acceptedEmpty')} hint={t('partners.acceptedEmptyHint')} />
        ) : (
          <div className="card-grid">
            {accepted.map(link => (
              <Card key={link.id} className="stack partner-card">
                <div className="partner-card__head">
                  <strong>{link.partnerDisplayName}</strong>
                  <Badge tone="gain">{t(`partners.status.${link.status}` as 'partners.status.accepted')}</Badge>
                </div>
                <p className="is-muted">
                  {link.initiatedByMe ? t('partners.initiated.byMe') : t('partners.initiated.byThem')}
                  {' · '}
                  {t('partners.linkedOn', { when: formatDate(link.createdAt, locale) })}
                </p>
                <label className="partner-share">
                  <input
                    type="checkbox"
                    checked={link.myShareDiaries}
                    disabled={busyId === link.id || share.isPending}
                    onChange={e => { void onToggleShare(link, e.target.checked) }}
                  />
                  <span>{t('partners.share.mine')}</span>
                </label>
                <p className="is-muted">
                  {link.partnerShareDiaries ? t('partners.share.theirsOn') : t('partners.share.theirsOff')}
                </p>
                <div className="partner-card__actions">
                  <Link className="btn btn--subtle btn--sm" to={`/partners/${link.id}/compare`}><span className="btn__label">{t('partners.compare.open')}</span></Link>
                  <Button size="sm" variant="ghost" loading={busyId === link.id} onClick={() => { void onRevoke(link) }}>
                    {t('partners.revoke')}
                  </Button>
                </div>
              </Card>
            ))}
          </div>
        )}
      </section>

      {pending.length > 0 ? (
        <section className="stack">
          <h2>{t('partners.pendingTitle')}</h2>
          <div className="card-grid">
            {pending.map(link => (
              <Card key={link.id} className="stack">
                <div className="partner-card__head">
                  <strong>{link.partnerDisplayName}</strong>
                  <Badge tone="muted">{t('partners.status.pending')}</Badge>
                </div>
                <p className="is-muted">
                  {link.initiatedByMe ? t('partners.pending.waitingThem') : t('partners.pending.waitingYou')}
                </p>
                <div className="partner-card__actions">
                  {!link.initiatedByMe ? (
                    <Button size="sm" variant="primary" loading={busyId === link.id} onClick={() => { void onAccept(link) }}>
                      {t('partners.accept')}
                    </Button>
                  ) : null}
                  <Button size="sm" variant="ghost" loading={busyId === link.id} onClick={() => { void onRevoke(link) }}>
                    {t('partners.revoke')}
                  </Button>
                </div>
              </Card>
            ))}
          </div>
        </section>
      ) : null}

      <Card className="stack">
        <h2>{t('partners.agent.title')}</h2>
        <p className="is-muted">{t('partners.agent.hint')}</p>
        <form className="inline-form" onSubmit={onCreateAgent}>
          <TextInput aria-label={t('partners.agent.name')} required value={agentName} onChange={e => setAgentName(e.target.value)} placeholder={t('partners.agent.name')} />
          <Button variant="primary" type="submit" loading={createAgent.isPending}>{t('partners.agent.create')}</Button>
        </form>
        {agentKey ? (
          <div className="secret-once" role="status">
            <code>{agentKey}</code>
            <Button size="sm" onClick={() => { void navigator.clipboard.writeText(agentKey) }}>{t('partners.agent.copy')}</Button>
            <Button size="sm" variant="ghost" onClick={() => setAgentKey('')}>{t('partners.agent.saved')}</Button>
          </div>
        ) : null}
      </Card>
    </>
  )
}

export function PartnerComparePage() {
  const { partnerId = '' } = useParams()
  const [params, setParams] = useSearchParams()
  const { t, locale } = useI18n()
  const defaults = useMemo(() => defaultRange(), [])
  const from = params.get('from') || defaults.from
  const to = params.get('to') || defaults.to
  const q = usePartnerCompareQuery(partnerId, from, to)
  const data = q.data

  function setRange(nextFrom: string, nextTo: string) {
    const next = new URLSearchParams(params)
    next.set('from', nextFrom)
    next.set('to', nextTo)
    setParams(next, { replace: true })
  }

  if (q.isLoading) return <PageSkeleton rows={3} />
  if (q.isError || !data) {
    return (
      <>
        <PageHeader title={t('partners.compare.title')} subtitle={t('partners.compare.unavailable')} />
        <SectionError onRetry={() => { void q.refetch() }} />
        <p><Link to="/partners">{t('common.back')}</Link></p>
      </>
    )
  }

  return (
    <>
      <PageHeader
        title={t('partners.compare.title')}
        subtitle={t('partners.compare.with', { name: data.partnerDisplayName })}
      />
      <Card className="stack partner-compare-controls">
        <div className="inline-form">
          <Field label={t('partners.compare.from')}>
            <TextInput type="date" value={from} onChange={e => setRange(e.target.value, to)} aria-label={t('partners.compare.from')} />
          </Field>
          <Field label={t('partners.compare.to')}>
            <TextInput type="date" value={to} onChange={e => setRange(from, e.target.value)} aria-label={t('partners.compare.to')} />
          </Field>
          <Button size="sm" variant="ghost" onClick={() => setRange(defaults.from, defaults.to)}>{t('partners.compare.reset')}</Button>
        </div>
        <p className="is-muted" role="status">{capabilityMessage(data, t)}</p>
      </Card>

      {data.days.length === 0 ? (
        <EmptyBox title={t('partners.compare.empty')} hint={t('partners.compare.emptyHint')} />
      ) : (
        <div className="partner-compare-days">
          {data.days.map(day => (
            <section key={day.localDate} className="partner-compare-day">
              <h2 className="partner-compare-day__date">{formatDay(day.localDate, locale)}</h2>
              <div className="partner-compare-columns">
                <div className="partner-compare-col">
                  <h3>{t('partners.compare.mine')}</h3>
                  {day.mine.length === 0
                    ? <p className="is-muted">{t('partners.compare.noEntry')}</p>
                    : day.mine.map(entry => <DiaryCard key={entry.id} title={entry.title} content={entry.content} tags={entry.tags} />)}
                </div>
                <div className="partner-compare-col">
                  <h3>{data.partnerDisplayName}</h3>
                  {data.capabilities.partnerDiaries === 'not_shared'
                    ? <p className="is-muted">{t('partners.compare.notShared')}</p>
                    : data.capabilities.partnerDiaries === 'unavailable'
                      ? <p className="is-muted">{t('partners.compare.partnerUnavailable')}</p>
                      : day.partner.length === 0
                        ? <p className="is-muted">{t('partners.compare.noEntry')}</p>
                        : day.partner.map(entry => <DiaryCard key={entry.id} title={entry.title} content={entry.content} tags={entry.tags} />)}
                </div>
              </div>
            </section>
          ))}
        </div>
      )}
      <p><Link to="/partners">{t('common.back')}</Link></p>
    </>
  )
}

function DiaryCard({ title, content, tags }: { title: string; content: string; tags: string[] }) {
  return (
    <Card className="stack partner-diary-entry">
      <strong>{title}</strong>
      <MarkdownView content={content} className="prose" />
      {tags.length > 0 ? (
        <div className="partner-tags">
          {tags.map(tag => <Badge key={tag} tone="muted">{tag}</Badge>)}
        </div>
      ) : null}
    </Card>
  )
}

function capabilityMessage(data: PartnerCompare, t: (key: Parameters<ReturnType<typeof useI18n>['t']>[0], vars?: Record<string, string | number>) => string) {
  switch (data.capabilities.partnerDiaries) {
    case 'available':
      return t('partners.compare.cap.available')
    case 'not_shared':
      return t('partners.compare.cap.notShared')
    default:
      return t('partners.compare.cap.unavailable')
  }
}
