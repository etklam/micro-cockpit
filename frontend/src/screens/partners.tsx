import { useMemo, useRef, useState, type FormEvent } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import type { PartnerCompare, PartnerInvitation, PartnerLink } from '../features/api'
import { partnerCompareErrorKind } from '../features/api'
import {
  useAcceptPartnerMutation,
  useBootstrapQuery,
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

/** Real calendar local date only; impossible dates → ''. No timezone shift. */
function validLocalDate(value: string | null | undefined): string {
  if (!value) return ''
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return ''
  const [y, m, d] = value.split('-').map(Number)
  const dt = new Date(Date.UTC(y, m - 1, d))
  if (dt.getUTCFullYear() !== y || dt.getUTCMonth() !== m - 1 || dt.getUTCDate() !== d) return ''
  return value
}

/** 30-day inclusive window ending on account local today (YYYY-MM-DD). Calendar math only. */
export function defaultRangeFromLocalDate(today: string): { from: string; to: string } {
  const to = validLocalDate(today)
  if (!to) return { from: '', to: '' }
  const [y, m, d] = to.split('-').map(Number)
  const end = new Date(Date.UTC(y, m - 1, d))
  const start = new Date(end)
  start.setUTCDate(start.getUTCDate() - 29)
  return { from: start.toISOString().slice(0, 10), to }
}

function inclusiveDayCount(from: string, to: string): number {
  const [fy, fm, fd] = from.split('-').map(Number)
  const [ty, tm, td] = to.split('-').map(Number)
  return Math.round((Date.UTC(ty, tm - 1, td) - Date.UTC(fy, fm - 1, fd)) / 86_400_000) + 1
}

async function copyText(text: string): Promise<boolean> {
  try {
    if (navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(text)
      return true
    }
  } catch {
    // fall through to execCommand
  }
  try {
    const ta = document.createElement('textarea')
    ta.value = text
    ta.setAttribute('readonly', '')
    ta.style.position = 'fixed'
    ta.style.left = '-9999px'
    document.body.appendChild(ta)
    ta.select()
    const ok = document.execCommand('copy')
    document.body.removeChild(ta)
    return ok
  } catch {
    return false
  }
}

export function PartnersPage() {
  const { t, format } = useI18n()
  const bootstrap = useBootstrapQuery()
  const timeZone = bootstrap.data?.timezone
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
  const [partnersError, setPartnersError] = useState('')
  const [invitesError, setInvitesError] = useState('')
  const [busyId, setBusyId] = useState<string | null>(null)
  const [copyHint, setCopyHint] = useState('')
  // Sync guards — isPending lags one tick behind double-clicks.
  const mutateGuard = useRef(false)

  const items = partners.data?.items ?? []
  const invites = invitations.data?.items ?? []
  const accepted = items.filter(x => x.status === 'accepted')
  const pending = items.filter(x => x.status === 'pending')

  async function onCreateInvite() {
    if (mutateGuard.current || createInvite.isPending || rawCode) return
    mutateGuard.current = true
    setInvitesError('')
    setCopyHint('')
    try {
      const created = await createInvite.mutateAsync()
      setRawCode(created.code)
    } catch {
      setInvitesError(t('partners.error.createInvite'))
    } finally {
      mutateGuard.current = false
    }
  }

  async function onRedeem(e: FormEvent) {
    e.preventDefault()
    if (mutateGuard.current || redeem.isPending) return
    mutateGuard.current = true
    setPartnersError('')
    setInvitesError('')
    try {
      await redeem.mutateAsync(redeemCode.trim())
      setRedeemCode('')
    } catch {
      setInvitesError(t('partners.error.redeem'))
    } finally {
      mutateGuard.current = false
    }
  }

  async function onRevokeInvite(item: PartnerInvitation) {
    if (mutateGuard.current || busyId || revokeInvite.isPending) return
    const ok = await confirm({
      title: t('partners.invite.revokeTitle'),
      message: t('partners.invite.revokeMessage'),
      confirmText: t('partners.invite.revoke'),
      tone: 'danger',
    })
    if (!ok) return
    mutateGuard.current = true
    setBusyId(item.id)
    setInvitesError('')
    try { await revokeInvite.mutateAsync(item.id) }
    catch { setInvitesError(t('partners.error.revokeInvite')) }
    finally { setBusyId(null); mutateGuard.current = false }
  }

  async function onAccept(link: PartnerLink) {
    if (mutateGuard.current || busyId || accept.isPending) return
    mutateGuard.current = true
    setBusyId(link.id)
    setPartnersError('')
    try { await accept.mutateAsync(link.id) }
    catch { setPartnersError(t('partners.error.accept')) }
    finally { setBusyId(null); mutateGuard.current = false }
  }

  async function onRevoke(link: PartnerLink) {
    if (mutateGuard.current || busyId || revoke.isPending) return
    const ok = await confirm({
      title: t('partners.revokeTitle'),
      message: t('partners.revokeMessage', { name: link.partnerDisplayName || t('partners.displayFallback') }),
      confirmText: t('partners.revoke'),
      tone: 'danger',
    })
    if (!ok) return
    mutateGuard.current = true
    setBusyId(link.id)
    setPartnersError('')
    try { await revoke.mutateAsync(link.id) }
    catch { setPartnersError(t('partners.error.revoke')) }
    finally { setBusyId(null); mutateGuard.current = false }
  }

  async function onToggleShare(link: PartnerLink, next: boolean) {
    if (mutateGuard.current || busyId || share.isPending) return
    mutateGuard.current = true
    setBusyId(link.id)
    setPartnersError('')
    try { await share.mutateAsync({ id: link.id, shareDiaries: next }) }
    catch { setPartnersError(t('partners.error.share')) }
    finally { setBusyId(null); mutateGuard.current = false }
  }

  async function onCreateAgent(e: FormEvent) {
    e.preventDefault()
    if (mutateGuard.current || createAgent.isPending) return
    mutateGuard.current = true
    setPartnersError('')
    setAgentKey('')
    setCopyHint('')
    try {
      const x = await createAgent.mutateAsync(agentName.trim())
      setAgentKey(x.apiKey)
      setAgentName('')
    } catch {
      setPartnersError(t('partners.error.createAgent'))
    } finally {
      mutateGuard.current = false
    }
  }

  async function onCopy(text: string, failKey: 'partners.error.copy' | 'partners.agent.copyFailed') {
    setCopyHint('')
    const ok = await copyText(text)
    if (!ok) setCopyHint(t(failKey))
  }

  const bootLoading = bootstrap.isLoading
  const partnersLoading = partners.isLoading
  const invitesLoading = invitations.isLoading
  if (bootLoading || (partnersLoading && invitesLoading && !partners.data && !invitations.data)) {
    return <PageSkeleton rows={3} />
  }

  return (
    <>
      {confirmNode}
      <PageHeader title={t('partners.title')} subtitle={t('partners.subtitle')} />
      {copyHint ? <p className="form-error" role="alert">{copyHint}</p> : null}

      <Card className="stack">
        <h2>{t('partners.invite.createTitle')}</h2>
        <p className="is-muted">{t('partners.invite.createHint')}</p>
        {invitesError ? <p className="form-error" role="alert">{invitesError}</p> : null}
        <div className="inline-form">
          <Button variant="primary" onClick={() => { void onCreateInvite() }} loading={createInvite.isPending} disabled={createInvite.isPending || !!rawCode}>
            {t('partners.invite.create')}
          </Button>
        </div>
        {rawCode ? (
          <div className="secret-once" role="status">
            <code>{rawCode}</code>
            <Button size="sm" onClick={() => { void onCopy(rawCode, 'partners.error.copy') }}>{t('partners.invite.copy')}</Button>
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
            disabled={redeem.isPending}
          />
          <Button variant="primary" type="submit" loading={redeem.isPending} disabled={redeem.isPending}>{t('partners.invite.redeem')}</Button>
        </form>
      </Card>

      <section className="stack">
        <h2>{t('partners.invite.openTitle')}</h2>
        {invitations.isError ? (
          <SectionError onRetry={() => { void invitations.refetch() }} />
        ) : invitesLoading && !invitations.data ? (
          <PageSkeleton rows={1} />
        ) : invites.length === 0 ? (
          <EmptyBox title={t('partners.invite.openEmpty')} hint={t('partners.invite.openEmptyHint')} />
        ) : (
          <div className="card-grid">
            {invites.map(item => (
              <Card key={item.id} className="stack">
                <div><Badge tone="muted">{t('partners.status.pending')}</Badge></div>
                <p className="is-muted">{t('partners.invite.expires', { when: format.dateTime(item.expiresAt, timeZone) })}</p>
                <Button size="sm" variant="ghost" loading={busyId === item.id} disabled={!!busyId || revokeInvite.isPending} onClick={() => { void onRevokeInvite(item) }}>
                  {t('partners.invite.revoke')}
                </Button>
              </Card>
            ))}
          </div>
        )}
      </section>

      <section className="stack">
        <h2>{t('partners.acceptedTitle')}</h2>
        {partnersError ? <p className="form-error" role="alert">{partnersError}</p> : null}
        {partners.isError ? (
          <SectionError onRetry={() => { void partners.refetch() }} />
        ) : partnersLoading && !partners.data ? (
          <PageSkeleton rows={1} />
        ) : accepted.length === 0 ? (
          <EmptyBox title={t('partners.acceptedEmpty')} hint={t('partners.acceptedEmptyHint')} />
        ) : (
          <div className="card-grid">
            {accepted.map(link => (
              <Card key={link.id} className="stack partner-card">
                <div className="partner-card__head">
                  <strong>{link.partnerDisplayName || t('partners.displayFallback')}</strong>
                  <Badge tone="gain">{t(`partners.status.${link.status}` as 'partners.status.accepted')}</Badge>
                </div>
                <p className="is-muted">
                  {link.initiatedByMe ? t('partners.initiated.byMe') : t('partners.initiated.byThem')}
                  {' · '}
                  {t('partners.linkedOn', { when: format.dateTime(link.acceptedAt ?? link.createdAt, timeZone) })}
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
                  <Button size="sm" variant="ghost" loading={busyId === link.id} disabled={!!busyId || revoke.isPending} onClick={() => { void onRevoke(link) }}>
                    {t('partners.revoke')}
                  </Button>
                </div>
              </Card>
            ))}
          </div>
        )}
      </section>

      {!partners.isError && pending.length > 0 ? (
        <section className="stack">
          <h2>{t('partners.pendingTitle')}</h2>
          <div className="card-grid">
            {pending.map(link => (
              <Card key={link.id} className="stack">
                <div className="partner-card__head">
                  <strong>{link.partnerDisplayName || t('partners.displayFallback')}</strong>
                  <Badge tone="muted">{t('partners.status.pending')}</Badge>
                </div>
                <p className="is-muted">
                  {link.initiatedByMe ? t('partners.pending.waitingThem') : t('partners.pending.waitingYou')}
                  {' · '}
                  {t('partners.linkedOn', { when: format.dateTime(link.createdAt, timeZone) })}
                </p>
                <div className="partner-card__actions">
                  {!link.initiatedByMe ? (
                    <Button size="sm" variant="primary" loading={busyId === link.id} disabled={!!busyId || accept.isPending} onClick={() => { void onAccept(link) }}>
                      {t('partners.accept')}
                    </Button>
                  ) : null}
                  <Button size="sm" variant="ghost" loading={busyId === link.id} disabled={!!busyId || revoke.isPending} onClick={() => { void onRevoke(link) }}>
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
          <TextInput aria-label={t('partners.agent.name')} required value={agentName} onChange={e => setAgentName(e.target.value)} placeholder={t('partners.agent.name')} disabled={createAgent.isPending} />
          <Button variant="primary" type="submit" loading={createAgent.isPending} disabled={createAgent.isPending}>{t('partners.agent.create')}</Button>
        </form>
        {agentKey ? (
          <div className="secret-once" role="status">
            <code>{agentKey}</code>
            <Button size="sm" onClick={() => { void onCopy(agentKey, 'partners.agent.copyFailed') }}>{t('partners.agent.copy')}</Button>
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
  const { t, format } = useI18n()
  const bootstrap = useBootstrapQuery()
  const accountToday = bootstrap.data?.currentLocalDate ?? ''
  const defaults = useMemo(() => defaultRangeFromLocalDate(accountToday), [accountToday])

  const rawFrom = params.get('from')
  const rawTo = params.get('to')
  const validFrom = validLocalDate(rawFrom)
  const validTo = validLocalDate(rawTo)
  const fromPresent = rawFrom != null && rawFrom !== ''
  const toPresent = rawTo != null && rawTo !== ''
  // Bad tokens in URL → invalid. Missing sides filled from account-local defaults.
  const hasBadToken = (fromPresent && !validFrom) || (toPresent && !validTo)
  const from = validFrom || defaults.from
  const to = validTo || defaults.to
  const inverted = !!from && !!to && from > to
  const tooLarge = !!from && !!to && !inverted && inclusiveDayCount(from, to) > 366
  const invalidDate = hasBadToken || inverted || tooLarge
  const invalidMessage = tooLarge ? t('partners.compare.rangeTooLarge') : t('partners.compare.invalidDate')
  const rangeReady = !invalidDate && !!from && !!to

  const q = usePartnerCompareQuery(partnerId, rangeReady ? from : '', rangeReady ? to : '')
  const data = q.data

  function setRange(nextFrom: string, nextTo: string) {
    const next = new URLSearchParams(params)
    const vf = validLocalDate(nextFrom)
    const vt = validLocalDate(nextTo)
    if (vf) next.set('from', vf)
    else next.delete('from')
    if (vt) next.set('to', vt)
    else next.delete('to')
    setParams(next, { replace: true })
  }

  if (bootstrap.isLoading) return <PageSkeleton rows={3} />
  if (bootstrap.isError || !accountToday) {
    return (
      <>
        <PageHeader title={t('partners.compare.title')} subtitle={t('partners.compare.unavailable')} />
        <SectionError onRetry={() => { void bootstrap.refetch() }} />
        <p><Link to="/partners">{t('common.back')}</Link></p>
      </>
    )
  }

  if (invalidDate) {
    return (
      <>
        <PageHeader title={t('partners.compare.title')} subtitle={invalidMessage} />
        <Card className="stack partner-compare-controls">
          <div className="inline-form">
            <Field label={t('partners.compare.from')}>
              <TextInput type="date" value={validFrom} onChange={e => setRange(e.target.value, validTo || defaults.to)} aria-label={t('partners.compare.from')} />
            </Field>
            <Field label={t('partners.compare.to')}>
              <TextInput type="date" value={validTo} onChange={e => setRange(validFrom || defaults.from, e.target.value)} aria-label={t('partners.compare.to')} />
            </Field>
            <Button size="sm" variant="ghost" onClick={() => setRange(defaults.from, defaults.to)} disabled={!defaults.from}>{t('partners.compare.reset')}</Button>
          </div>
          <p className="form-error" role="alert">{invalidMessage}</p>
        </Card>
        <p><Link to="/partners">{t('common.back')}</Link></p>
      </>
    )
  }

  if (q.isLoading) return <PageSkeleton rows={3} />

  if (q.isError || !data) {
    const kind = partnerCompareErrorKind(q.error)
    const subtitle =
      kind === 'missing' ? t('partners.compare.missing')
        : kind === 'auth' ? t('partners.compare.auth')
          : kind === 'invalid_range' ? invalidMessage
            : t('partners.compare.unavailable')
    return (
      <>
        <PageHeader title={t('partners.compare.title')} subtitle={subtitle} />
        <SectionError onRetry={() => { void q.refetch() }} />
        <p><Link to="/partners">{t('common.back')}</Link></p>
      </>
    )
  }

  return (
    <>
      <PageHeader
        title={t('partners.compare.title')}
        subtitle={t('partners.compare.with', { name: data.partnerDisplayName || t('partners.displayFallback') })}
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
              <h2 className="partner-compare-day__date">{format.date(day.localDate)}</h2>
              <div className="partner-compare-columns">
                <div className="partner-compare-col">
                  <h3>{t('partners.compare.mine')}</h3>
                  {day.mine.length === 0
                    ? <p className="is-muted">{t('partners.compare.noEntry')}</p>
                    : day.mine.map(entry => (
                      <DiaryCard key={entry.id} title={entry.title || format.empty} content={entry.content} tags={entry.tags} />
                    ))}
                </div>
                <div className="partner-compare-col">
                  <h3>{data.partnerDisplayName || t('partners.displayFallback')}</h3>
                  {data.capabilities.partnerDiaries === 'not_shared'
                    ? <p className="is-muted">{t('partners.compare.notShared')}</p>
                    : data.capabilities.partnerDiaries === 'unavailable'
                      ? <p className="is-muted">{t('partners.compare.partnerUnavailable')}</p>
                      : day.partner.length === 0
                        ? <p className="is-muted">{t('partners.compare.noEntry')}</p>
                        : day.partner.map(entry => (
                          <DiaryCard key={entry.id} title={entry.title || format.empty} content={entry.content} tags={entry.tags} />
                        ))}
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

/** Read-only diary card — no edit controls; markdown is sanitized by MarkdownView. */
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
