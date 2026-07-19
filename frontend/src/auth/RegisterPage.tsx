import { useState } from 'react'
import type { FormEvent } from 'react'
import { Link, Navigate, useLocation, useNavigate } from 'react-router-dom'
import * as session from '../api'
import { ApiError } from '../generated/edge'
import { useAuth } from './AuthProvider'
import { Brand, Button, Field, TextInput, ThemeToggle } from '../ui'

const defaultTimezone = () => Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC'
const destination = (state: unknown) => (state as { from?: string } | null)?.from ?? '/today'

export function RegisterPage() {
  const { state, login } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
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
      setError(registerMessage(err)); setBusy(false); return
    }
    try {
      await login(trimmedEmail, password)
      navigate(from, { replace: true })
    } catch {
      setError('Account created. Please sign in.')
      setCreatedNeedsSignIn(true)
      setBusy(false)
    }
  }

  return (
    <main className="login">
      <div className="login__glow" aria-hidden="true" />
      <div className="login__theme">
        <ThemeToggle />
      </div>
      <form className="login__card" onSubmit={submit}>
        <Brand />
        <div className="login__head">
          <h1>Create your cockpit.</h1>
          <p>Start a diary-first trading record with one quiet account.</p>
        </div>
        <div className="login__signal" aria-hidden="true">
          <span />
          <span />
          <span />
        </div>
        <Field label="Name">
          <TextInput autoComplete="name" required maxLength={100} value={displayName} onChange={event => setDisplayName(event.target.value)} placeholder="Your name" />
        </Field>
        <Field label="Email">
          <TextInput type="email" autoComplete="username" required maxLength={254} value={email} onChange={event => setEmail(event.target.value)} placeholder="you@example.com" />
        </Field>
        <Field label="Password" hint="Use at least 12 characters.">
          <TextInput type="password" autoComplete="new-password" minLength={12} maxLength={256} required value={password} onChange={event => setPassword(event.target.value)} placeholder="••••••••••••" />
        </Field>
        {error ? <p className="login__error" role="alert">{error}</p> : null}
        {createdNeedsSignIn ? (
          <p className="login__foot"><Link to="/login" state={location.state}>Continue to sign in</Link></p>
        ) : null}
        <Button variant="primary" block type="submit" loading={busy} disabled={busy}>{busy ? null : 'Create account'}</Button>
        <p className="login__foot">Already have an account? <Link to="/login" state={location.state}>Sign in</Link>.</p>
      </form>
    </main>
  )
}

function registerMessage(err: unknown): string {
  if (err instanceof ApiError) {
    if (err.status === 400) return 'Check your email, name, and password. Passwords must be at least 12 characters.'
    if (err.status === 404) return 'Registration is not available on this deployment.'
    if (err.status === 409) return 'Unable to create this account. Try signing in if you may already be registered.'
    if (err.status === 429) return 'Too many attempts. Wait a few minutes and try again.'
  }
  return 'Could not create the account. Try again.'
}
