import { useState } from 'react'
import type { FormEvent } from 'react'
import { Link, Navigate, useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from './AuthProvider'
import { Brand, Button, Field, TextInput, ThemeToggle } from '../ui'
import { useI18n } from '../i18n'

export function LoginPage() {
  const { state, login } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const { t, setLocale, locale } = useI18n()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  if (state === 'authenticated') return <Navigate to="/today" replace />

  async function submit(event: FormEvent) {
    event.preventDefault(); setBusy(true); setError('')
    try {
      await login(email, password)
      const from = (location.state as { from?: string } | null)?.from ?? '/today'
      navigate(from, { replace: true })
    } catch { setError(t('auth.login.mismatch')) } finally { setBusy(false) }
  }

  return (
    <main className="login">
      <div className="login__glow" aria-hidden="true" />
      <div className="login__theme">
        <LanguageToggle locale={locale} onChange={next => { void setLocale(next) }} />
        <ThemeToggle />
      </div>
      <form className="login__card" onSubmit={submit}>
        <Brand />
        <div className="login__head">
          <h1>{t('auth.login.title')}</h1>
          <p>{t('auth.login.subtitle')}</p>
        </div>
        <div className="login__signal" aria-hidden="true">
          <span />
          <span />
          <span />
        </div>
        <Field label={t('auth.login.email')}>
          <TextInput type="email" autoComplete="username" required value={email} onChange={event => setEmail(event.target.value)} placeholder="you@example.com" />
        </Field>
        <Field label={t('auth.login.password')}>
          <TextInput type="password" autoComplete="current-password" required value={password} onChange={event => setPassword(event.target.value)} placeholder="••••••••" />
        </Field>
        {error ? <p className="login__error" role="alert">{error}</p> : null}
        <Button variant="primary" block type="submit" loading={busy}>{busy ? null : t('auth.login.submit')}</Button>
        <p className="login__foot">{t('auth.login.noAccount')} <Link to="/register" state={location.state}>{t('auth.login.createOne')}</Link>.</p>
        <p className="login__foot"><Link to="/">{t('brand.name')}</Link> · <Link to="/tools">{t('nav.tools')}</Link></p>
      </form>
    </main>
  )
}

function LanguageToggle({ locale, onChange }: { locale: 'en' | 'zh-Hant'; onChange: (l: 'en' | 'zh-Hant') => void }) {
  return (
    <div className="lang-toggle" role="group" aria-label="Language">
      <button type="button" className={locale === 'en' ? 'is-active' : undefined} onClick={() => onChange('en')}>EN</button>
      <button type="button" className={locale === 'zh-Hant' ? 'is-active' : undefined} onClick={() => onChange('zh-Hant')}>繁</button>
    </div>
  )
}
