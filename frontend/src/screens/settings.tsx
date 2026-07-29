import { useEffect, useMemo, useState } from 'react'
import type { FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import {
  isAppearance,
  normalizeAccent,
  reconcileAccent,
  reconcileAppearance,
} from '../features/appearance'
import { useAppearance } from '../features/useAppearance'
import { deviceTimezone, formatTimezoneLabel } from '../features/accountTime'
import {
  useAgentsQuery,
  useAccessGrantsQuery,
  useBootstrapQuery,
  useCreateAccessGrantMutation,
  useCreateAgentMutation,
  useRevokeAgentTokenMutation,
  useRevokeAccessGrantMutation,
  useRotateAgentTokenMutation,
  useSaveSettingsMutation,
  useSettingsQuery,
} from '../features/queries'
import { deleteAccount, getAccountExport } from '../features/api'
import { useAuth } from '../auth/AuthProvider'
import { Button, Card, Field, PageHeader, SelectBox, TextInput, ThemeSwitches, useConfirm } from '../ui'
import { PageSkeleton, SectionError } from '../shell'
import { cx } from '../format'
import { isLocale, useI18n, type Locale } from '../i18n'

const COMMON_TIMEZONES = [
  'UTC',
  'America/New_York',
  'America/Chicago',
  'America/Denver',
  'America/Los_Angeles',
  'America/Sao_Paulo',
  'Europe/London',
  'Europe/Paris',
  'Europe/Berlin',
  'Africa/Johannesburg',
  'Asia/Dubai',
  'Asia/Kolkata',
  'Asia/Singapore',
  'Asia/Hong_Kong',
  'Asia/Shanghai',
  'Asia/Taipei',
  'Asia/Tokyo',
  'Asia/Seoul',
  'Australia/Sydney',
  'Pacific/Auckland',
]

export function SettingsPage() {
  const settings = useSettingsQuery()
  const bootstrap = useBootstrapQuery()
  const save = useSaveSettingsMutation()
  const agents = useAgentsQuery()
  const grants = useAccessGrantsQuery()
  const createAgent = useCreateAgentMutation()
  const createGrant = useCreateAccessGrantMutation()
  const rotateAgent = useRotateAgentTokenMutation()
  const revokeAgent = useRevokeAgentTokenMutation()
  const revokeGrant = useRevokeAccessGrantMutation()
  const { preference: appearance, scheme, accent, setAppearance, setAccent } = useAppearance()
  const { locale, setLocale, t } = useI18n()
  const { logout } = useAuth()
  const { confirm, confirmNode } = useConfirm()
  const navigate = useNavigate()
  const deviceTz = useMemo(() => deviceTimezone(), [])

  const [displayName, setDisplayName] = useState('')
  const [timezone, setTimezone] = useState('')
  const [journalDayRollover, setJournalDayRollover] = useState('00:00')
  const [timezoneCustom, setTimezoneCustom] = useState(false)
  const [baseCurrency, setBaseCurrency] = useState('USD')
  const [formError, setFormError] = useState('')
  const [partialNotice, setPartialNotice] = useState('')
  const [saved, setSaved] = useState(false)
  const [agentName, setAgentName] = useState('')
  const [agentError, setAgentError] = useState('')
  const [agentToken, setAgentToken] = useState('')
  const [busyAgentId, setBusyAgentId] = useState('')
  const [grantAgentId, setGrantAgentId] = useState('')
  const [grantMode, setGrantMode] = useState<'fixed' | 'ongoing'>('fixed')
  const [grantFrom, setGrantFrom] = useState('')
  const [grantTo, setGrantTo] = useState('')
  const [grantScope, setGrantScope] = useState<'all' | 'broad_market' | 'sector' | 'theme' | 'instrument'>('all')
  const [grantSubject, setGrantSubject] = useState('')
  const [grantExpiry, setGrantExpiry] = useState('')
  const [accountBusy, setAccountBusy] = useState<'export' | 'delete' | ''>('')
  const [accountError, setAccountError] = useState('')

  const localeOptions: { value: Locale; label: string }[] = [
    { value: 'en', label: t('settings.language.en') },
    { value: 'zh-Hant', label: t('settings.language.zhHant') },
  ]

  useEffect(() => {
    if (!settings.data) return
    setDisplayName(settings.data.displayName)
    setTimezone(settings.data.timezone)
    setJournalDayRollover(settings.data.journalDayRollover)
    setTimezoneCustom(!COMMON_TIMEZONES.includes(settings.data.timezone))
    setBaseCurrency(settings.data.baseCurrency)
  }, [settings.data])

  if (settings.isLoading || bootstrap.isLoading) return <PageSkeleton rows={3} />
  if (settings.isError || !settings.data) return <SectionError onRetry={() => { void settings.refetch() }} />

  const tzMismatch = timezone && deviceTz && timezone !== deviceTz
  const formatAgentTime = (value: string | null) => value
    ? new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value))
    : t('settings.agents.never')

  async function onCreateAgent(e: FormEvent) {
    e.preventDefault()
    const name = agentName.trim()
    if (!name || createAgent.isPending) return
    setAgentError('')
    setAgentToken('')
    try {
      const result = await createAgent.mutateAsync(name)
      setAgentToken(result.apiToken)
      setAgentName('')
    } catch {
      setAgentError(t('settings.agents.error'))
    }
  }

  async function onRotateAgent(id: string) {
    if (busyAgentId) return
    setBusyAgentId(id)
    setAgentError('')
    setAgentToken('')
    try {
      const result = await rotateAgent.mutateAsync(id)
      setAgentToken(result.apiToken)
    } catch {
      setAgentError(t('settings.agents.error'))
    } finally {
      setBusyAgentId('')
    }
  }

  async function onRevokeAgent(id: string) {
    if (busyAgentId) return
    setBusyAgentId(id)
    setAgentError('')
    setAgentToken('')
    try {
      await revokeAgent.mutateAsync(id)
    } catch {
      setAgentError(t('settings.agents.error'))
    } finally {
      setBusyAgentId('')
    }
  }

  async function onCreateGrant(e: FormEvent) {
    e.preventDefault()
    if (!grantAgentId || !grantFrom || !grantTo || createGrant.isPending) return
    setAgentError('')
    try {
      await createGrant.mutateAsync({
        agentUserId: grantAgentId,
        mode: grantMode,
        from: grantFrom,
        to: grantTo,
        subjectType: grantScope !== 'all' && grantScope !== 'instrument' ? grantScope : null,
        subject: grantScope !== 'all' && grantScope !== 'instrument' ? grantSubject.trim() : null,
        instrumentId: grantScope === 'instrument' ? grantSubject.trim() : null,
        expiresAt: grantExpiry ? new Date(grantExpiry).toISOString() : null,
      })
      setGrantSubject('')
    } catch {
      setAgentError(t('settings.agents.grantError'))
    }
  }

  async function onRevokeGrant(id: string) {
    if (busyAgentId) return
    setBusyAgentId(id)
    setAgentError('')
    try {
      await revokeGrant.mutateAsync(id)
    } catch {
      setAgentError(t('settings.agents.grantError'))
    } finally {
      setBusyAgentId('')
    }
  }

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    if (save.isPending) return
    setFormError('')
    setPartialNotice('')
    setSaved(false)
    const name = displayName.trim()
    const tz = timezone.trim()
    const ccy = baseCurrency.trim().toUpperCase()
    setDisplayName(name)
    setBaseCurrency(ccy)
    if (name.length < 1 || name.length > 100) { setFormError(t('settings.error.displayName')); return }
    if (!tz || tz.length > 100) { setFormError(t('settings.error.timezone')); return }
    if (!/^(?:[01]\d|2[0-3]):[0-5]\d$/.test(journalDayRollover)) { setFormError(t('settings.error.rollover')); return }
    if (!/^[A-Z]{3}$/.test(ccy)) { setFormError(t('settings.error.currency')); return }
    if (!isAppearance(appearance)) { setFormError(t('settings.error.appearance')); return }
    if (!isLocale(locale)) { setFormError(t('settings.error.locale')); return }

    try {
      const result = await save.mutateAsync({
        displayName: name,
        timezone: tz,
        journalDayRollover,
        baseCurrency: ccy,
        appearance,
        locale,
        accentTheme: accent,
      })
      if (result.status === 'saved_session_stale') {
        setPartialNotice(t('settings.sessionStale'))
        await logout()
        navigate('/login', { replace: true, state: { notice: t('settings.sessionStale') } })
        return
      }
      setSaved(true)
      setDisplayName(result.settings.displayName)
      setTimezone(result.settings.timezone)
      setJournalDayRollover(result.settings.journalDayRollover)
      setBaseCurrency(result.settings.baseCurrency)
      if (isAppearance(result.settings.appearance)) reconcileAppearance(result.settings.appearance)
      const serverAccent = normalizeAccent(result.settings.accentTheme)
      if (serverAccent) reconcileAccent(serverAccent)
    } catch (error) {
      setFormError(error instanceof Error ? error.message : t('settings.error.save'))
    }
  }

  async function onExport() {
    if (accountBusy) return
    setAccountBusy('export')
    setAccountError('')
    try {
      const data = await getAccountExport()
      const url = URL.createObjectURL(new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' }))
      const link = document.createElement('a')
      link.href = url
      link.download = `micro-cockpit-export-${new Date().toISOString().slice(0, 10)}.json`
      link.click()
      URL.revokeObjectURL(url)
    } catch {
      setAccountError(t('settings.account.error'))
    } finally {
      setAccountBusy('')
    }
  }

  async function onDeleteAccount() {
    if (accountBusy || !await confirm({
      title: t('settings.account.deleteConfirmTitle'),
      message: t('settings.account.deleteConfirmMessage'),
      confirmText: t('settings.account.delete'),
      tone: 'danger',
    })) return
    setAccountBusy('delete')
    setAccountError('')
    try {
      await deleteAccount()
      await logout()
      navigate('/login', { replace: true })
    } catch {
      setAccountError(t('settings.account.error'))
      setAccountBusy('')
    }
  }

  return (
    <>
      <PageHeader title={t('settings.title')} subtitle={t('settings.subtitle')} />
      <Card className="settings-form">
        <form className="stack" onSubmit={onSubmit}>
          <section className="stack">
            <h2>{t('settings.profile')}</h2>
            <Field label={t('settings.email')} hint={t('settings.emailHint')}>
              <TextInput value={settings.data.email} readOnly disabled />
            </Field>
            <Field label={t('settings.displayName')}>
              <TextInput required maxLength={100} value={displayName} onChange={e => setDisplayName(e.target.value)} />
            </Field>
          </section>

          <section className="stack">
            <h2>{t('settings.regional')}</h2>
            <Field label={t('settings.language')} hint={t('settings.languageHint')}>
              <div className="theme-picker" role="radiogroup" aria-label={t('settings.language')}>
                {localeOptions.map(option => {
                  const selected = locale === option.value
                  return (
                    <button
                      key={option.value}
                      type="button"
                      role="radio"
                      aria-checked={selected}
                      className={cx('theme-picker__option', selected && 'is-selected')}
                      onClick={() => {
                        void setLocale(option.value)
                        setSaved(false)
                      }}
                    >
                      <span className="theme-picker__copy">
                        <span className="theme-picker__label">{option.label}</span>
                      </span>
                    </button>
                  )
                })}
              </div>
            </Field>
            <Field label={t('settings.timezone')} hint={t('settings.deviceTimezone', { timezone: formatTimezoneLabel(deviceTz) })}>
              {timezoneCustom ? (
                <TextInput
                  required
                  list="iana-timezones"
                  value={timezone}
                  onChange={e => setTimezone(e.target.value)}
                  placeholder={t('settings.timezonePlaceholder')}
                />
              ) : (
                <SelectBox value={COMMON_TIMEZONES.includes(timezone) ? timezone : ''} onChange={e => {
                  if (e.target.value === '__custom') { setTimezoneCustom(true); return }
                  setTimezone(e.target.value)
                }}>
                  <option value="" disabled>{t('settings.chooseTimezone')}</option>
                  {COMMON_TIMEZONES.map(z => <option key={z} value={z}>{z}</option>)}
                  <option value="__custom">{t('settings.otherTimezone')}</option>
                </SelectBox>
              )}
              <datalist id="iana-timezones">
                {COMMON_TIMEZONES.map(z => <option key={z} value={z} />)}
              </datalist>
            </Field>
            {tzMismatch ? (
              <p className="form-hint" role="status">
                {t('settings.timezoneMismatch', { account: timezone, device: deviceTz })}
              </p>
            ) : null}
            <Field label={t('settings.rollover')} hint={t('settings.rolloverHint')}>
              <TextInput type="time" required value={journalDayRollover} onChange={event => setJournalDayRollover(event.target.value)} />
            </Field>
            <Field label={t('settings.baseCurrency')} hint={t('settings.baseCurrencyHint')}>
              <TextInput required maxLength={3} value={baseCurrency} onChange={e => setBaseCurrency(e.target.value.toUpperCase())} />
            </Field>
          </section>

          <section className="stack">
            <h2>{t('settings.appearance')}</h2>
            <Field label={t('settings.theme.controlsLabel')} hint={t('settings.themeHint')}>
              <ThemeSwitches
                scheme={scheme}
                accent={accent}
                onSchemeChange={next => { void setAppearance(next); setSaved(false) }}
                onAccentChange={next => { void setAccent(next); setSaved(false) }}
              />
            </Field>
          </section>

          {formError ? <p className="form-error" role="alert">{formError}</p> : null}
          {partialNotice ? <p className="form-error" role="alert">{partialNotice}</p> : null}
          {saved ? <p className="form-hint" role="status">{t('settings.saved')}</p> : null}
          <div className="form-actions">
            <Button variant="primary" type="submit" loading={save.isPending}>{t('settings.save')}</Button>
          </div>
        </form>
      </Card>
      <Card className="settings-form stack">
        <section className="stack">
          <h2>{t('settings.agents.title')}</h2>
          <p className="form-hint">{t('settings.agents.hint')}</p>
          <form className="inline-form" onSubmit={onCreateAgent}>
            <TextInput
              aria-label={t('settings.agents.name')}
              required
              maxLength={100}
              value={agentName}
              onChange={event => setAgentName(event.target.value)}
              placeholder={t('settings.agents.name')}
              disabled={createAgent.isPending}
            />
            <Button variant="primary" type="submit" loading={createAgent.isPending}>
              {t('settings.agents.create')}
            </Button>
          </form>
          {agentToken ? (
            <div className="secret-once" role="status">
              <code>{agentToken}</code>
              <Button size="sm" variant="ghost" onClick={() => setAgentToken('')}>{t('settings.agents.saved')}</Button>
            </div>
          ) : null}
          {agentError ? <p className="form-error" role="alert">{agentError}</p> : null}
          {agents.isLoading ? <p className="form-hint">{t('common.loading')}</p> : null}
          {agents.isError ? <SectionError onRetry={() => { void agents.refetch() }} /> : null}
          {!agents.isLoading && !agents.isError && agents.data?.items.length === 0
            ? <p className="form-hint">{t('settings.agents.empty')}</p>
            : null}
          {agents.data?.items.map(agent => (
            <Card key={agent.userId} className="stack">
              <h3>{agent.displayName}</h3>
              <p className="form-hint">{agent.scopes.join(', ')}</p>
              <dl className="detail-list">
                <div><dt>{t('settings.agents.created')}</dt><dd>{formatAgentTime(agent.tokenCreatedAt)}</dd></div>
                <div><dt>{t('settings.agents.lastUsed')}</dt><dd>{formatAgentTime(agent.lastUsedAt)}</dd></div>
                <div><dt>{t('settings.agents.lastSuccess')}</dt><dd>{formatAgentTime(agent.lastSuccessfulRequestAt)}</dd></div>
              </dl>
              <div className="form-actions">
                <Button size="sm" onClick={() => { void onRotateAgent(agent.userId) }} loading={busyAgentId === agent.userId && rotateAgent.isPending}>
                  {t('settings.agents.rotate')}
                </Button>
                <Button size="sm" variant="ghost" onClick={() => { void onRevokeAgent(agent.userId) }} disabled={!!busyAgentId || !agent.keyId}>
                  {t('settings.agents.revoke')}
                </Button>
              </div>
            </Card>
          ))}
        </section>
        <section className="stack">
          <h2>{t('settings.agents.grantsTitle')}</h2>
          <p className="form-hint">{t('settings.agents.revokeWarning')}</p>
          <form className="stack" onSubmit={onCreateGrant}>
            <Field label={t('settings.agents.grantAgent')}>
              <SelectBox required value={grantAgentId} onChange={event => setGrantAgentId(event.target.value)}>
                <option value="" disabled>{t('settings.agents.grantAgentPlaceholder')}</option>
                {agents.data?.items.map(agent => <option key={agent.userId} value={agent.userId}>{agent.displayName}</option>)}
              </SelectBox>
            </Field>
            <Field label={t('settings.agents.grantMode')}>
              <SelectBox value={grantMode} onChange={event => setGrantMode(event.target.value as 'fixed' | 'ongoing')}>
                <option value="fixed">{t('settings.agents.grantFixed')}</option>
                <option value="ongoing">{t('settings.agents.grantOngoing')}</option>
              </SelectBox>
            </Field>
            <div className="inline-form">
              <Field label={t('settings.agents.grantFrom')}><TextInput type="date" required value={grantFrom} onChange={event => setGrantFrom(event.target.value)} /></Field>
              <Field label={t('settings.agents.grantTo')}><TextInput type="date" required value={grantTo} onChange={event => setGrantTo(event.target.value)} /></Field>
            </div>
            <Field label={t('settings.agents.grantScope')}>
              <SelectBox value={grantScope} onChange={event => { setGrantScope(event.target.value as typeof grantScope); setGrantSubject('') }}>
                <option value="all">{t('settings.agents.grantAll')}</option>
                <option value="broad_market">{t('today.observations.subject.broadMarket')}</option>
                <option value="sector">{t('today.observations.subject.sector')}</option>
                <option value="theme">{t('today.observations.subject.theme')}</option>
                <option value="instrument">{t('today.observations.subject.instrument')}</option>
              </SelectBox>
            </Field>
            {grantScope !== 'all' ? (
              <Field label={grantScope === 'instrument' ? t('settings.agents.grantInstrument') : t('settings.agents.grantSubject')}>
                <TextInput required value={grantSubject} onChange={event => setGrantSubject(event.target.value)} />
              </Field>
            ) : null}
            <Field label={t('settings.agents.grantExpiry')} hint={t('settings.agents.grantExpiryHint')}>
              <TextInput type="datetime-local" value={grantExpiry} onChange={event => setGrantExpiry(event.target.value)} />
            </Field>
            <div className="form-actions">
              <Button variant="primary" type="submit" loading={createGrant.isPending}>{t('settings.agents.grantCreate')}</Button>
            </div>
          </form>
          {grants.data?.items.length === 0 ? <p className="form-hint">{t('settings.agents.grantsEmpty')}</p> : null}
          {grants.data?.items.map(grant => {
            const agent = agents.data?.items.find(item => item.userId === grant.agentUserId)
            return (
              <Card key={grant.id} className="stack">
                <h3>{agent?.displayName ?? grant.agentUserId}</h3>
                <p>{grant.mode === 'fixed' ? t('settings.agents.grantFixed') : t('settings.agents.grantOngoing')} · {grant.from} — {grant.to}</p>
                <p className="form-hint">{grant.revokedAt ? t('settings.agents.grantRevoked') : t('settings.agents.grantActive')}</p>
                {!grant.revokedAt ? (
                  <Button size="sm" variant="ghost" onClick={() => { void onRevokeGrant(grant.id) }} loading={busyAgentId === grant.id}>
                    {t('settings.agents.grantRevoke')}
                  </Button>
                ) : null}
              </Card>
            )
          })}
        </section>
      </Card>
      <Card className="settings-form stack">
        <section className="stack">
          <h2>{t('settings.account.title')}</h2>
          <p className="form-hint">{t('settings.account.exportHint')}</p>
          <div className="form-actions">
            <Button onClick={() => { void onExport() }} loading={accountBusy === 'export'}>
              {t('settings.account.export')}
            </Button>
          </div>
          <p className="form-hint">{t('settings.account.deleteHint')}</p>
          <div className="form-actions">
            <Button variant="danger" onClick={() => { void onDeleteAccount() }} loading={accountBusy === 'delete'}>
              {t('settings.account.delete')}
            </Button>
          </div>
          {accountError ? <p className="form-error" role="alert">{accountError}</p> : null}
        </section>
      </Card>
      {confirmNode}
    </>
  )
}
