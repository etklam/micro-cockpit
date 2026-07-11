import { createContext, useContext, useState } from 'react'
import type { FormEvent } from 'react'
import * as api from './api'
import { Brand, Button, ErrorBox, Field, IconButton, TextInput, useConfirm } from './ui'
import type { ConfirmOpts } from './ui'
import { Icon } from './icons'
import type { IconName } from './icons'
import { cx } from './format'
import './App.css'
import { TodayPage, DiaryPage, CalendarPage, DisciplinePage, AlertsPage } from './pages'

export type Page = 'today' | 'diary' | 'calendar' | 'discipline' | 'alerts'

const NAV: { id: Page; label: string; icon: IconName }[] = [
  { id: 'today', label: 'Today', icon: 'today' },
  { id: 'diary', label: 'Diary', icon: 'diary' },
  { id: 'calendar', label: 'Calendar', icon: 'calendar' },
  { id: 'discipline', label: 'Discipline', icon: 'compass' },
  { id: 'alerts', label: 'Alerts', icon: 'bell' },
]

type Cockpit = { go: (p: Page) => void; confirm: (o: ConfirmOpts) => Promise<boolean> }
const Ctx = createContext<Cockpit>(null!)
export const useCockpit = () => useContext(Ctx)

export default function App() {
  const [authed, setAuthed] = useState(() => Boolean(localStorage.getItem('accessToken')))
  const [page, setPage] = useState<Page>('today')
  const { confirm, confirmNode } = useConfirm()

  if (!authed) return <Login onDone={() => setAuthed(true)} />

  const signOut = () => { localStorage.removeItem('accessToken'); setAuthed(false) }
  const go = (p: Page) => { setPage(p); scrollTo({ top: 0, behavior: 'smooth' }) }

  return (
    <Ctx.Provider value={{ go, confirm }}>
      <div className="shell">
        <Sidebar page={page} onNav={go} onSignOut={signOut} />
        <div className="main">
          <MobileTop onSignOut={signOut} />
          <main className="content" id="content">
            {page === 'today' && <TodayPage />}
            {page === 'diary' && <DiaryPage />}
            {page === 'calendar' && <CalendarPage />}
            {page === 'discipline' && <DisciplinePage />}
            {page === 'alerts' && <AlertsPage />}
          </main>
        </div>
        <MobileNav page={page} onNav={go} />
        {confirmNode}
      </div>
    </Ctx.Provider>
  )
}

/* ----------------------------- Sidebar ----------------------------- */
function Sidebar({ page, onNav, onSignOut }: { page: Page; onNav: (p: Page) => void; onSignOut: () => void }) {
  return (
    <aside className="sidebar" aria-label="Primary">
      <Brand />
      <nav className="nav" aria-label="Sections">
        {NAV.map((n) => (
          <button
            key={n.id}
            type="button"
            className={cx('nav__item', page === n.id && 'is-active')}
            aria-current={page === n.id ? 'page' : undefined}
            onClick={() => onNav(n.id)}
          >
            <Icon name={n.icon} size={18} />
            <span>{n.label}</span>
          </button>
        ))}
      </nav>
      <div className="sidebar__foot">
        <Button variant="ghost" icon="logout" onClick={onSignOut} className="signout-btn">Sign out</Button>
      </div>
    </aside>
  )
}

/* --------------------------- Mobile chrome ------------------------- */
function MobileTop({ onSignOut }: { onSignOut: () => void }) {
  return (
    <header className="mobile-top">
      <Brand compact />
      <IconButton icon="logout" label="Sign out" onClick={onSignOut} />
    </header>
  )
}

function MobileNav({ page, onNav }: { page: Page; onNav: (p: Page) => void }) {
  return (
    <nav className="mobile-nav" aria-label="Sections">
      {NAV.map((n) => (
        <button
          key={n.id}
          type="button"
          className={cx('mobile-nav__item', page === n.id && 'is-active')}
          aria-current={page === n.id ? 'page' : undefined}
          onClick={() => onNav(n.id)}
        >
          <Icon name={n.icon} size={20} />
          <span>{n.label}</span>
        </button>
      ))}
    </nav>
  )
}

/* ------------------------------ Login ------------------------------ */
function Login({ onDone }: { onDone: () => void }) {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError('')
    try {
      await api.login(email, password)
      onDone()
    } catch {
      setError('That email and password didn’t match.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <main className="login">
      <div className="login__glow" aria-hidden="true" />
      <form className="login__card" onSubmit={submit}>
        <Brand />
        <div className="login__head">
          <h1>Your decisions, remembered.</h1>
          <p>Sign in to your trade journal.</p>
        </div>
        <Field label="Email">
          <TextInput type="email" autoComplete="username" required value={email}
            onChange={(e) => setEmail(e.target.value)} placeholder="you@example.com" />
        </Field>
        <Field label="Password">
          <TextInput type="password" autoComplete="current-password" required value={password}
            onChange={(e) => setPassword(e.target.value)} placeholder="••••••••" />
        </Field>
        {error ? <p className="login__error" role="alert">{error}</p> : null}
        <Button variant="primary" block type="submit" loading={busy}>
          {busy ? null : 'Sign in'}
        </Button>
        <p className="login__foot">A diary-first trade journal.</p>
      </form>
    </main>
  )
}

/* ----------------------- Shared loading/error ---------------------- */
export function SectionError({ onRetry }: { onRetry: () => void }) {
  return <ErrorBox message="Couldn’t reach the cockpit." onRetry={onRetry} />
}
export function PageSkeleton({ rows = 3 }: { rows?: number }) {
  return (
    <div className="page-skel">
      <div className="skel" style={{ height: 34, width: 220 }} />
      <div className="card-grid">
        {Array.from({ length: 3 }, (_, i) => (
          <div className="card" key={i}>
            <div className="skel" style={{ height: 12, width: 90 }} />
            <div className="skel" style={{ height: 30, width: 120, marginTop: 16 }} />
          </div>
        ))}
      </div>
      {Array.from({ length: rows }, (_, i) => (
        <div className="skel" key={i} style={{ height: 56, marginTop: 12 }} />
      ))}
    </div>
  )
}
