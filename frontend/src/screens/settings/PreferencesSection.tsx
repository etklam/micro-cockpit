import type { FormEvent } from 'react'
import type { Accent, Appearance, ColorScheme } from '../../features/appearance'
import { formatTimezoneLabel } from '../../features/accountTime'
import { Button, Card, Field, SelectBox, TextInput, ThemeSwitches } from '../../ui'
import { cx } from '../../format'
import { useI18n, type Locale } from '../../i18n'
import { COMMON_TIMEZONES } from './constants'

type PreferencesSectionProps = {
  email: string
  displayName: string
  setDisplayName: (value: string) => void
  timezone: string
  setTimezone: (value: string) => void
  timezoneCustom: boolean
  setTimezoneCustom: (value: boolean) => void
  journalDayRollover: string
  setJournalDayRollover: (value: string) => void
  baseCurrency: string
  setBaseCurrency: (value: string) => void
  deviceTz: string
  scheme: ColorScheme
  accent: Accent
  setAppearance: (value: Appearance) => Promise<void>
  setAccent: (value: Accent) => Promise<void>
  locale: Locale
  setLocale: (value: Locale) => Promise<void>
  onSubmit: (event: FormEvent<HTMLFormElement>) => void
  saving: boolean
  formError: string
  partialNotice: string
  saved: boolean
  setSaved: (value: boolean) => void
}

export function PreferencesSection({
  email, displayName, setDisplayName, timezone, setTimezone, timezoneCustom, setTimezoneCustom,
  journalDayRollover, setJournalDayRollover, baseCurrency, setBaseCurrency, deviceTz, scheme, accent,
  setAppearance, setAccent, locale, setLocale, onSubmit, saving, formError, partialNotice, saved,
  setSaved,
}: PreferencesSectionProps) {
  const { t } = useI18n()
  const localeOptions: { value: Locale; label: string }[] = [
    { value: 'en', label: t('settings.language.en') },
    { value: 'zh-Hant', label: t('settings.language.zhHant') },
  ]
  const tzMismatch = timezone && deviceTz && timezone !== deviceTz

  return <Card className="settings-form">
    <form className="stack" onSubmit={onSubmit}>
      <section className="stack">
        <h2>{t('settings.profile')}</h2>
        <Field label={t('settings.email')} hint={t('settings.emailHint')}><TextInput value={email} readOnly disabled /></Field>
        <Field label={t('settings.displayName')}><TextInput required maxLength={100} value={displayName} onChange={event => setDisplayName(event.target.value)} /></Field>
      </section>
      <section className="stack">
        <h2>{t('settings.regional')}</h2>
        <Field label={t('settings.language')} hint={t('settings.languageHint')}>
          <div className="theme-picker" role="radiogroup" aria-label={t('settings.language')}>
            {localeOptions.map(option => {
              const selected = locale === option.value
              return <button key={option.value} type="button" role="radio" aria-checked={selected} className={cx('theme-picker__option', selected && 'is-selected')} onClick={() => { void setLocale(option.value); setSaved(false) }}>
                <span className="theme-picker__copy"><span className="theme-picker__label">{option.label}</span></span>
              </button>
            })}
          </div>
        </Field>
        <Field label={t('settings.timezone')} hint={t('settings.deviceTimezone', { timezone: formatTimezoneLabel(deviceTz) })}>
          {timezoneCustom ? <TextInput required list="iana-timezones" value={timezone} onChange={event => setTimezone(event.target.value)} placeholder={t('settings.timezonePlaceholder')} /> : <SelectBox value={COMMON_TIMEZONES.some(value => value === timezone) ? timezone : ''} onChange={event => {
            if (event.target.value === '__custom') { setTimezoneCustom(true); return }
            setTimezone(event.target.value)
          }}>
            <option value="" disabled>{t('settings.chooseTimezone')}</option>
            {COMMON_TIMEZONES.map(value => <option key={value} value={value}>{value}</option>)}
            <option value="__custom">{t('settings.otherTimezone')}</option>
          </SelectBox>}
          <datalist id="iana-timezones">{COMMON_TIMEZONES.map(value => <option key={value} value={value} />)}</datalist>
        </Field>
        {tzMismatch ? <p className="form-hint" role="status">{t('settings.timezoneMismatch', { account: timezone, device: deviceTz })}</p> : null}
        <Field label={t('settings.rollover')} hint={t('settings.rolloverHint')}><TextInput type="time" required value={journalDayRollover} onChange={event => setJournalDayRollover(event.target.value)} /></Field>
        <Field label={t('settings.baseCurrency')} hint={t('settings.baseCurrencyHint')}><TextInput required maxLength={3} value={baseCurrency} onChange={event => setBaseCurrency(event.target.value.toUpperCase())} /></Field>
      </section>
      <section className="stack">
        <h2>{t('settings.appearance')}</h2>
        <Field label={t('settings.theme.controlsLabel')} hint={t('settings.themeHint')}><ThemeSwitches scheme={scheme} accent={accent} onSchemeChange={next => { void setAppearance(next); setSaved(false) }} onAccentChange={next => { void setAccent(next); setSaved(false) }} /></Field>
      </section>
      {formError ? <p className="form-error" role="alert">{formError}</p> : null}
      {partialNotice ? <p className="form-error" role="alert">{partialNotice}</p> : null}
      {saved ? <p className="form-hint" role="status">{t('settings.saved')}</p> : null}
      <div className="form-actions"><Button variant="primary" type="submit" loading={saving}>{t('settings.save')}</Button></div>
    </form>
  </Card>
}
