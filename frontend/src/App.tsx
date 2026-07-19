import { useEffect } from 'react'
import { Navigate, NavLink, Outlet, Route, Routes, useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from './auth/AuthProvider'
import { LoginPage } from './auth/LoginPage'
import { RegisterPage } from './auth/RegisterPage'
import { Brand, Button, ErrorBox, IconButton, ThemeToggle, useConfirm } from './ui'
import { Icon } from './icons'
import { cx } from './format'
import './App.css'
import { AlertsPage, CalendarPage, DiaryDetailPage, DiaryPage, DisciplinePage, TodayPage } from './pages'
import { ArticleDetailPage, ArticlesPage, MorePage, PartnersPage, PriceAlertsPage, RotationPage, ToolsPage, WatchlistPage } from './latePages'
import { SettingsPage } from './screens/settings'
import { useBootstrapQuery } from './features/queries'
import { reconcileAppearance, subscribeSystemAppearance, type Appearance, isAppearance } from './features/appearance'
import { accountMonthYear } from './features/accountTime'
import { MonthlyReviewPage, MonthlyReviewRedirect } from './MonthlyReviewPage'
import { CockpitProvider, SectionError, type Page } from './shell'

const PATHS: Record<Page, string> = {
  today: '/today', diary: '/diary', calendar: '/calendar', discipline: '/discipline', alerts: '/alerts',
  more: '/more', review: '/review', settings: '/settings', watchlist: '/watchlist', 'price-alerts': '/price-alerts', rotation: '/rotation', partners: '/partners',
  articles: '/articles', tools: '/tools',
}
const NAV: { id: Page; label: string; icon: Parameters<typeof Icon>[0]['name'] }[] = [
  { id: 'today', label: 'Today', icon: 'today' },
  { id: 'diary', label: 'Diary', icon: 'diary' },
  { id: 'calendar', label: 'Calendar', icon: 'calendar' },
  { id: 'discipline', label: 'Discipline', icon: 'compass' },
  { id: 'alerts', label: 'Alerts', icon: 'bell' },
]
const MORE: { id: Page; label: string }[] = [
  { id: 'review', label: 'Monthly review' },
  { id: 'settings', label: 'Settings' },
  { id: 'watchlist', label: 'Watchlist' }, { id: 'price-alerts', label: 'Price alerts' },
  { id: 'rotation', label: 'Market rotation' }, { id: 'partners', label: 'Partners' },
  { id: 'articles', label: 'Articles' }, { id: 'tools', label: 'Tools' },
]

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/register" element={<RegisterPage />} />
      <Route element={<RequireAuth />}>
        <Route element={<Shell />}>
          <Route index element={<Navigate to="/today" replace />} />
          <Route path="/today" element={<TodayPage />} />
          <Route path="/diary" element={<DiaryPage />} />
          <Route path="/diary/:diaryId" element={<DiaryDetailPage />} />
          <Route path="/calendar" element={<CalendarRedirect />} />
          <Route path="/calendar/:year/:month" element={<CalendarPage />} />
          <Route path="/discipline" element={<DisciplinePage />} />
          <Route path="/alerts" element={<AlertsPage />} />
          <Route path="/more" element={<MorePage />} />
          <Route path="/settings" element={<SettingsPage />} />
          <Route path="/review" element={<MonthlyReviewRedirect />} />
          <Route path="/review/:year/:month" element={<MonthlyReviewPage />} />
          <Route path="/watchlist" element={<WatchlistPage />} />
          <Route path="/price-alerts" element={<PriceAlertsPage />} />
          <Route path="/rotation" element={<RotationPage />} />
          <Route path="/partners" element={<PartnersPage />} />
          <Route path="/articles" element={<ArticlesPage />} />
          <Route path="/articles/:slug" element={<ArticleDetailPage />} />
          <Route path="/tools" element={<ToolsPage />} />
          <Route path="*" element={<NotFoundPage />} />
        </Route>
      </Route>
    </Routes>
  )
}

function RequireAuth() {
  const { state } = useAuth()
  const location = useLocation()
  if (state === 'restoring') return null
  if (state === 'anonymous') return <Navigate to="/login" replace state={{ from: location.pathname }} />
  return <Outlet />
}

type BootstrapData = NonNullable<ReturnType<typeof useBootstrapQuery>['data']>

function Shell() {
  const navigate = useNavigate()
  const { logout } = useAuth()
  const { confirm, confirmNode } = useConfirm()
  const bootstrap = useBootstrapQuery()
  useEffect(() => {
    if (!bootstrap.data) return
    const appearance = isAppearance(bootstrap.data.appearance) ? bootstrap.data.appearance as Appearance : 'system'
    reconcileAppearance(appearance)
    return subscribeSystemAppearance(() => appearance)
  }, [bootstrap.data])
  if (bootstrap.isLoading) return null
  if (bootstrap.isError || !bootstrap.data) return <SectionError onRetry={() => { void bootstrap.refetch() }} />
  const go = (page: Page) => {
    if (page === 'calendar') {
      const ym = accountMonthYear(bootstrap.data.currentLocalDate)
      navigate(ym ? `/calendar/${ym.year}/${String(ym.month).padStart(2, '0')}` : '/calendar')
    } else {
      navigate(PATHS[page])
    }
    scrollTo({ top: 0, behavior: 'smooth' })
  }
  return (
    <CockpitProvider value={{ go, confirm }}>
      <a className="skip-link" href="#content">Skip to content</a>
      <div className="shell">
        <Sidebar cockpit={bootstrap.data} onSignOut={logout} />
        <div className="main">
          <MobileTop cockpit={bootstrap.data} onSignOut={logout} />
          <main className="content" id="content"><Outlet /></main>
        </div>
        <MobileNav />
        {confirmNode}
      </div>
    </CockpitProvider>
  )
}

function CalendarRedirect() {
  const bootstrap = useBootstrapQuery()
  if (bootstrap.isLoading || !bootstrap.data) return null
  const ym = accountMonthYear(bootstrap.data.currentLocalDate)
  if (!ym) return <SectionError onRetry={() => { void bootstrap.refetch() }} />
  return <Navigate to={`/calendar/${ym.year}/${String(ym.month).padStart(2, '0')}`} replace />
}

function Sidebar({ cockpit, onSignOut }: { cockpit: BootstrapData; onSignOut: () => void }) {
  const location = useLocation()
  const moreOpen = MORE.some(item => location.pathname.startsWith(PATHS[item.id]))
  return (
    <aside className="sidebar" aria-label="Primary">
      <Brand />
      <div className="sidebar__body">
        <div className="sidebar__label">Reflect</div>
        <nav className="nav" aria-label="Primary sections">
          {NAV.map(item => <NavItem key={item.id} item={item} />)}
        </nav>
        <details className="more-nav" open={moreOpen}>
          <summary>Decision support</summary>
          <div className="more-nav__list">
            {MORE.map(item => (
              <NavLink key={item.id} className={({ isActive }) => cx('nav__item', isActive && 'is-active')} to={PATHS[item.id]}>
                {item.label}
              </NavLink>
            ))}
          </div>
        </details>
      </div>
      <div className="sidebar__foot">
        <div className="sidebar__identity" aria-label="Signed in user">
          <strong>{cockpit.currentUser.displayName || cockpit.currentUser.email}</strong>
          <span>{cockpit.baseCurrency} · {cockpit.timezone}</span>
        </div>
        <div className="sidebar__tools">
          <ThemeToggle />
          <Button variant="ghost" icon="logout" onClick={onSignOut} className="signout-btn">Sign out</Button>
        </div>
      </div>
    </aside>
  )
}

function NavItem({ item }: { item: (typeof NAV)[number] }) {
  return <NavLink to={PATHS[item.id]} className={({ isActive }) => cx('nav__item', isActive && 'is-active')}><Icon name={item.icon} size={18} /><span>{item.label}</span></NavLink>
}

function MobileTop({ cockpit, onSignOut }: { cockpit: BootstrapData; onSignOut: () => void }) {
  return (
    <header className="mobile-top">
      <Brand compact />
      <span className="mobile-top__meta">{cockpit.currentLocalDate}</span>
      <div className="mobile-top__actions">
        <ThemeToggle />
        <IconButton icon="logout" label="Sign out" onClick={onSignOut} />
      </div>
    </header>
  )
}
function MobileNav() {
  const mobile = NAV.slice(0, 4)
  const location = useLocation()
  const moreActive = location.pathname.startsWith('/more') || location.pathname.startsWith('/alerts') || MORE.some(item => location.pathname.startsWith(PATHS[item.id]))
  return <nav className="mobile-nav" aria-label="Sections">{mobile.map(item => <NavLink key={item.id} to={PATHS[item.id]} className={({ isActive }) => cx('mobile-nav__item', isActive && 'is-active')}><Icon name={item.icon} size={20} /><span>{item.label}</span></NavLink>)}<NavLink to="/more" className={cx('mobile-nav__item', moreActive && 'is-active')}><Icon name="layers" size={20} /><span>More</span></NavLink></nav>
}

function NotFoundPage() { return <ErrorBox message="Page not found." /> }
