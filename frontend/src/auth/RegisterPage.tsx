import { useState } from 'react'
import type { FormEvent } from 'react'
import { Link, Navigate, useLocation, useNavigate } from 'react-router-dom'
import * as session from '../api'
import { useAuth } from './AuthProvider'
import { Brand, Button, Field, TextInput, ThemeToggle } from '../ui'
import { registerErrorMessage, useI18n } from '../i18n'

const defaultTimezone = () => Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC'
const destination = (state: unknown) => (state as { from?: string } | null)?.from ?? '/today'

export function RegisterPage() {
  const { state, login } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const { t, locale, setLocale } = useI18n()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [error, setError] = useState('')
  const [createdNeedsSignIn, setCreatedNeedsSignIn] = useState(false)
  const [busy, setBusy] = useState(false)
  if (state === 'authenticated') return <Navigate to="/today" replace />

  async function submit(event: FormEvent) {
    event.preventDefault(); setBusy(true); setError(''); setCreatedNeedsSignIn(false)
    const trimmedEmail = email.trim()
    const trimmedName = displayName.trim()
    const from = destination(location.state)
    try {
      await session.register({ email: trimmedEmail, password, displayName: trimmedName, timezone: defaultTimezone(), baseCurrency: 'USD' })
    } catch (err) {
      setError(registerErrorMessage(locale, err)); setBusy(false); return
    }
    try {
      await login(trimmedEmail, password)
      navigate(from, { replace: true })
    } catch {
      setError(t('auth.register.createdSignIn'))
      setCreatedNeedsSignIn(true)
      setBusy(false)
    }
  }

  return (
    <main className="login">
      <div className="login__glow" aria-hidden="true" />
      <div className="login__theme">
        <div className="lang-toggle" role="group" aria-label="Language">
          <button type="button" className={locale === 'en' ? 'is-active' : undefined} onClick={() => { void setLocale('en') }}>EN</button>
          <button type="button" className={locale === 'zh-Hant' ? 'is-active' : undefined} onClick={() => { void setLocale('zh-Hant') }}>繁</button>
        </div>
        <ThemeToggle />
      </div>
      <form className="login__card" onSubmit={submit}>
        <Brand />
        <div className="login__head">
          <h1>{t('auth.register.title')}</h1>
          <p>{t('auth.register.subtitle')}</p>
        </div>
        <div className="login__signal" aria-hidden="true">
          <span />
          <span />
          <span />
        </div>
        <Field label={t('auth.register.name')}>
          <TextInput autoComplete="name" required maxLength={100} value={displayName} onChange={event => setDisplayName(event.target.value)} placeholder={t('auth.register.namePlaceholder')} />
        </Field>
        <Field label={t('auth.register.email')}>
          <TextInput type="email" autoComplete="username" required maxLength={254} value={email} onChange={event => setEmail(event.target.value)} placeholder={t('auth.register.emailPlaceholder')} />
        </Field>
        <Field label={t('auth.register.password')} hint={t('auth.register.passwordHint')}>
          <TextInput type="password" autoComplete="new-password" minLength={12} maxLength={256} required value={password} onChange={event => setPassword(event.target.value)} placeholder="••••••••••••" />
        </Field>
        {error ? <p className="login__error" role="alert">{error}</p> : null}
        {createdNeedsSignIn ? (
          <p className="login__foot"><Link to="/login" state={location.state}>{t('auth.register.continueSignIn')}</Link></p>
        ) : null}
        <Button variant="primary" block type="submit" loading={busy} disabled={busy}>{busy ? null : t('auth.register.submit')}</Button>
        <p className="login__foot">{t('auth.register.haveAccount')} <Link to="/login" state={location.state}>{t('auth.register.signIn')}</Link>.</p>
        <p className="login__foot"><Link to="/">{t('brand.name')}</Link> · <Link to="/tools">{t('nav.tools')}</Link></p>
      </form>
    </main>
  )
}
