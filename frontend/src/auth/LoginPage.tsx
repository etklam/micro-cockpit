import { useState } from 'react'
import type { FormEvent } from 'react'
import { Navigate, useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from './AuthProvider'
import { Brand, Button, Field, TextInput } from '../ui'

export function LoginPage() {
  const { state, login } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
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
    } catch { setError('That email and password didn’t match.') } finally { setBusy(false) }
  }

  return <main className="login"><div className="login__glow" aria-hidden="true" /><form className="login__card" onSubmit={submit}><Brand /><div className="login__head"><h1>Your decisions, remembered.</h1><p>Sign in to your trade journal.</p></div><Field label="Email"><TextInput type="email" autoComplete="username" required value={email} onChange={event => setEmail(event.target.value)} placeholder="you@example.com" /></Field><Field label="Password"><TextInput type="password" autoComplete="current-password" required value={password} onChange={event => setPassword(event.target.value)} placeholder="••••••••" /></Field>{error ? <p className="login__error" role="alert">{error}</p> : null}<Button variant="primary" block type="submit" loading={busy}>{busy ? null : 'Sign in'}</Button><p className="login__foot">A diary-first trade journal.</p></form></main>
}
