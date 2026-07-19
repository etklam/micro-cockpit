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
  const { preference: appearance, setAppearance } = useAppearance()
  const { locale, setLocale, t } = useI18n()
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

  const appearanceOptions: { value: Appearance; label: string; hint: string; icon: IconName; swatch: string }[] = [
    { value: 'system', label: t('settings.theme.system'), hint: t('settings.theme.systemHint'), icon: 'monitor', swatch: 'system' },
    { value: 'dark', label: t('settings.theme.dark'), hint: t('settings.theme.darkHint'), icon: 'moon', swatch: 'dark' },
    { value: 'light', label: t('settings.theme.light'), hint: t('settings.theme.lightHint'), icon: 'sun', swatch: 'light' },
  ]

  const localeOptions: { value: Locale; label: string }[] = [
    { value: 'en', label: t('settings.language.en') },
    { value: 'zh-Hant', label: t('settings.language.zhHant') },
  ]

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
    if (name.length < 1 || name.length > 100) { setFormError(t('settings.error.displayName')); return }
    if (!tz || tz.length > 100) { setFormError(t('settings.error.timezone')); return }
    if (!/^[A-Z]{3}$/.test(ccy)) { setFormError(t('settings.error.currency')); return }
    if (!isAppearance(appearance)) { setFormError(t('settings.error.appearance')); return }
    if (!isLocale(locale)) { setFormError(t('settings.error.locale')); return }

    try {
      const result = await save.mutateAsync({ displayName: name, timezone: tz, baseCurrency: ccy, appearance, locale })
      if (result.status === 'saved_session_stale') {
        setPartialNotice(t('settings.sessionStale'))
        await logout()
        navigate('/login', { replace: true, state: { notice: t('settings.sessionStale') } })
        return
      }
      setSaved(true)
      setDisplayName(result.settings.displayName)
      setTimezone(result.settings.timezone)
      setBaseCurrency(result.settings.baseCurrency)
      if (isAppearance(result.settings.appearance)) void setAppearance(result.settings.appearance)
      if (isLocale(result.settings.locale)) void setLocale(result.settings.locale)
    } catch (error) {
      setFormError(error instanceof Error ? error.message : t('settings.error.save'))
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
            <Field label={t('settings.baseCurrency')} hint={t('settings.baseCurrencyHint')}>
              <TextInput required maxLength={3} value={baseCurrency} onChange={e => setBaseCurrency(e.target.value.toUpperCase())} />
            </Field>
            <p className="form-hint">
              {t('settings.reminderNote')}
            </p>
          </section>

          <section className="stack">
            <h2>{t('settings.appearance')}</h2>
            <Field label={t('settings.theme')} hint={t('settings.themeHint')}>
              <div className="theme-picker" role="radiogroup" aria-label={t('settings.theme')}>
                {appearanceOptions.map(option => {
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
          {saved ? <p className="form-hint" role="status">{t('settings.saved')}</p> : null}
          <div className="form-actions">
            <Button variant="primary" type="submit" loading={save.isPending}>{t('settings.save')}</Button>
          </div>
        </form>
      </Card>
    </>
  )
}
