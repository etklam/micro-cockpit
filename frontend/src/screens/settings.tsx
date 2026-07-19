import { useEffect, useMemo, useState } from 'react'
import type { FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { isAppearance, type Appearance } from '../features/appearance'
import { useAppearance } from '../features/useAppearance'
import { deviceTimezone, formatTimezoneLabel } from '../features/accountTime'
import { useBootstrapQuery, useSaveSettingsMutation, useSettingsQuery } from '../features/queries'
import { useAuth } from '../auth/AuthProvider'
import { Button, Card, Field, PageHeader, SelectBox, TextInput } from '../ui'
import { Icon, type IconName } from '../icons'
import { PageSkeleton, SectionError } from '../shell'
import { cx } from '../format'

const APPEARANCE_OPTIONS: { value: Appearance; label: string; hint: string; icon: IconName; swatch: string }[] = [
  { value: 'system', label: 'System', hint: 'Match the device', icon: 'monitor', swatch: 'system' },
  { value: 'dark', label: 'Dark', hint: 'Evening instrument', icon: 'moon', swatch: 'dark' },
  { value: 'light', label: 'Light', hint: 'Day desk', icon: 'sun', swatch: 'light' },
]

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
  const { preference: appearance, setAppearance } = useAppearance()
  const { logout } = useAuth()
  const navigate = useNavigate()
  const deviceTz = useMemo(() => deviceTimezone(), [])

  const [displayName, setDisplayName] = useState('')
  const [timezone, setTimezone] = useState('')
  const [timezoneCustom, setTimezoneCustom] = useState(false)
  const [baseCurrency, setBaseCurrency] = useState('USD')
  const [formError, setFormError] = useState('')
  const [partialNotice, setPartialNotice] = useState('')
  const [saved, setSaved] = useState(false)

  useEffect(() => {
    if (!settings.data) return
    setDisplayName(settings.data.displayName)
    setTimezone(settings.data.timezone)
    setTimezoneCustom(!COMMON_TIMEZONES.includes(settings.data.timezone))
    setBaseCurrency(settings.data.baseCurrency)
  }, [settings.data])

  if (settings.isLoading || bootstrap.isLoading) return <PageSkeleton rows={3} />
  if (settings.isError || !settings.data) return <SectionError onRetry={() => { void settings.refetch() }} />

  const tzMismatch = timezone && deviceTz && timezone !== deviceTz

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
    if (name.length < 1 || name.length > 100) { setFormError('Display name must be 1–100 characters.'); return }
    if (!tz || tz.length > 100) { setFormError('Enter a valid IANA timezone.'); return }
    if (!/^[A-Z]{3}$/.test(ccy)) { setFormError('Base currency must be a three-letter code.'); return }
    if (!isAppearance(appearance)) { setFormError('Choose a valid appearance.'); return }

    try {
      const result = await save.mutateAsync({ displayName: name, timezone: tz, baseCurrency: ccy, appearance })
      if (result.status === 'saved_session_stale') {
        setPartialNotice(result.message)
        await logout()
        navigate('/login', { replace: true, state: { notice: result.message } })
        return
      }
      setSaved(true)
      setDisplayName(result.settings.displayName)
      setTimezone(result.settings.timezone)
      setBaseCurrency(result.settings.baseCurrency)
      if (isAppearance(result.settings.appearance)) void setAppearance(result.settings.appearance)
    } catch (error) {
      setFormError(error instanceof Error ? error.message : 'Could not save settings.')
    }
  }

  return (
    <>
      <PageHeader title="Settings" subtitle="Account preferences for this cockpit." />
      <Card className="settings-form">
        <form className="stack" onSubmit={onSubmit}>
          <section className="stack">
            <h2>Profile</h2>
            <Field label="Email" hint="Email changes are not available in this phase.">
              <TextInput value={settings.data.email} readOnly disabled />
            </Field>
            <Field label="Display name">
              <TextInput required maxLength={100} value={displayName} onChange={e => setDisplayName(e.target.value)} />
            </Field>
          </section>

          <section className="stack">
            <h2>Regional settings</h2>
            <Field label="Account timezone" hint={`Device timezone: ${formatTimezoneLabel(deviceTz)}`}>
              {timezoneCustom ? (
                <TextInput
                  required
                  list="iana-timezones"
                  value={timezone}
                  onChange={e => setTimezone(e.target.value)}
                  placeholder="Area/City"
                />
              ) : (
                <SelectBox value={COMMON_TIMEZONES.includes(timezone) ? timezone : ''} onChange={e => {
                  if (e.target.value === '__custom') { setTimezoneCustom(true); return }
                  setTimezone(e.target.value)
                }}>
                  <option value="" disabled>Choose timezone</option>
                  {COMMON_TIMEZONES.map(z => <option key={z} value={z}>{z}</option>)}
                  <option value="__custom">Other IANA timezone…</option>
                </SelectBox>
              )}
              <datalist id="iana-timezones">
                {COMMON_TIMEZONES.map(z => <option key={z} value={z} />)}
              </datalist>
            </Field>
            {tzMismatch ? (
              <p className="form-hint" role="status">
                Account timezone ({timezone}) differs from this device ({deviceTz}). Diary dates and trade times use the account timezone.
              </p>
            ) : null}
            <Field label="Base currency" hint="Default for new trades. Historical trades keep their own currency.">
              <TextInput required maxLength={3} value={baseCurrency} onChange={e => setBaseCurrency(e.target.value.toUpperCase())} />
            </Field>
            <p className="form-hint">
              Existing diary reminders keep the timezone stored when they were created. Delete and recreate a reminder to use the new account timezone.
            </p>
          </section>

          <section className="stack">
            <h2>Appearance</h2>
            <Field label="Theme" hint="Applied immediately. Save to keep it with your account.">
              <div className="theme-picker" role="radiogroup" aria-label="Theme">
                {APPEARANCE_OPTIONS.map(option => {
                  const selected = appearance === option.value
                  return (
                    <button
                      key={option.value}
                      type="button"
                      role="radio"
                      aria-checked={selected}
                      className={cx('theme-picker__option', selected && 'is-selected')}
                      onClick={() => {
                        void setAppearance(option.value)
                        setSaved(false)
                      }}
                    >
                      <span className={cx('theme-picker__swatch', `theme-picker__swatch--${option.swatch}`)} aria-hidden="true" />
                      <span className="theme-picker__copy">
                        <span className="theme-picker__label">
                          <Icon name={option.icon} size={14} style={{ display: 'inline-block', verticalAlign: '-2px', marginRight: 6 }} />
                          {option.label}
                        </span>
                        <span className="theme-picker__hint">{option.hint}</span>
                      </span>
                    </button>
                  )
                })}
              </div>
            </Field>
          </section>

          {formError ? <p className="form-error" role="alert">{formError}</p> : null}
          {partialNotice ? <p className="form-error" role="alert">{partialNotice}</p> : null}
          {saved ? <p className="form-hint" role="status">Settings saved.</p> : null}
          <div className="form-actions">
            <Button variant="primary" type="submit" loading={save.isPending}>Save settings</Button>
          </div>
        </form>
      </Card>
    </>
  )
}
