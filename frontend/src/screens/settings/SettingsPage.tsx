import { useEffect, useMemo, useState } from 'react'
import type { FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { isAppearance, normalizeAccent, reconcileAccent, reconcileAppearance } from '../../features/appearance'
import { useAppearance } from '../../features/useAppearance'
import { deviceTimezone } from '../../features/accountTime'
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
} from '../../features/queries'
import { deleteAccount, getAccountExport } from '../../features/api'
import { useAuth } from '../../auth/AuthProvider'
import { Card, PageHeader, useConfirm } from '../../ui'
import { PageSkeleton, SectionError } from '../../shell'
import { isLocale, useI18n } from '../../i18n'
import { COMMON_TIMEZONES } from './constants'
import { PreferencesSection } from './PreferencesSection'
import { AgentsSection } from './AgentsSection'
import { AccessGrantsSection } from './AccessGrantsSection'
import { AccountSection } from './AccountSection'

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

  useEffect(() => {
    if (!settings.data) return
    setDisplayName(settings.data.displayName)
    setTimezone(settings.data.timezone)
    setJournalDayRollover(settings.data.journalDayRollover)
    setTimezoneCustom(!COMMON_TIMEZONES.some(value => value === settings.data!.timezone))
    setBaseCurrency(settings.data.baseCurrency)
  }, [settings.data])

  if (settings.isLoading || bootstrap.isLoading) return <PageSkeleton rows={3} />
  if (settings.isError || !settings.data) return <SectionError onRetry={() => { void settings.refetch() }} />

  const formatAgentTime = (value: string | null) => value
    ? new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value))
    : t('settings.agents.never')

  async function onCreateAgent(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
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

  async function onCreateGrant(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
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

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
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
      const result = await save.mutateAsync({ displayName: name, timezone: tz, journalDayRollover, baseCurrency: ccy, appearance, locale, accentTheme: accent })
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

  return <>
    <PageHeader title={t('settings.title')} subtitle={t('settings.subtitle')} />
    <PreferencesSection
      email={settings.data.email}
      displayName={displayName} setDisplayName={setDisplayName}
      timezone={timezone} setTimezone={setTimezone} timezoneCustom={timezoneCustom} setTimezoneCustom={setTimezoneCustom}
      journalDayRollover={journalDayRollover} setJournalDayRollover={setJournalDayRollover}
      baseCurrency={baseCurrency} setBaseCurrency={setBaseCurrency} deviceTz={deviceTz}
      scheme={scheme} accent={accent} setAppearance={setAppearance} setAccent={setAccent}
      locale={locale} setLocale={setLocale} onSubmit={onSubmit} saving={save.isPending}
      formError={formError} partialNotice={partialNotice} saved={saved} setSaved={setSaved}
    />
    <Card className="settings-form stack">
      <AgentsSection
        agents={agents.data?.items} loading={agents.isLoading} error={agents.isError} onRetry={() => { void agents.refetch() }}
        agentName={agentName} setAgentName={setAgentName} onCreate={onCreateAgent} createPending={createAgent.isPending}
        agentToken={agentToken} clearAgentToken={() => setAgentToken('')} agentError={agentError} busyAgentId={busyAgentId}
        rotatePending={id => busyAgentId === id && rotateAgent.isPending} onRotate={id => { void onRotateAgent(id) }} onRevoke={id => { void onRevokeAgent(id) }} formatAgentTime={formatAgentTime}
      />
      <AccessGrantsSection
        agents={agents.data?.items} grants={grants.data?.items} grantAgentId={grantAgentId} setGrantAgentId={setGrantAgentId}
        grantMode={grantMode} setGrantMode={setGrantMode} grantFrom={grantFrom} setGrantFrom={setGrantFrom} grantTo={grantTo} setGrantTo={setGrantTo}
        grantScope={grantScope} setGrantScope={setGrantScope} grantSubject={grantSubject} setGrantSubject={setGrantSubject} grantExpiry={grantExpiry} setGrantExpiry={setGrantExpiry}
        onCreate={onCreateGrant} createPending={createGrant.isPending} busyAgentId={busyAgentId} onRevoke={id => { void onRevokeGrant(id) }}
      />
    </Card>
    <AccountSection busy={accountBusy} error={accountError} onExport={() => { void onExport() }} onDelete={() => { void onDeleteAccount() }} />
    {confirmNode}
  </>
}
