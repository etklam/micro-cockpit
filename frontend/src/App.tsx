import { useEffect, useId, useRef, useState, type KeyboardEvent as ReactKeyboardEvent, type ReactNode } from 'react'
import { Link, Navigate, NavLink, Outlet, Route, Routes, useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from './auth/AuthProvider'
import { LoginPage } from './auth/LoginPage'
import { RegisterPage } from './auth/RegisterPage'
import { Brand, Button, ErrorBox, IconButton, ThemeControls, ThemeToggle, useConfirm } from './ui'
import { Icon } from './icons'
import { cx } from './format'
import './App.css'
import { AlertsPage, CalendarPage, DiaryDetailPage, DiaryPage, DisciplinePage, TodayPage } from './pages'
import { ArticleDetailPage, ArticlesPage, MorePage, PartnerComparePage, PartnersPage, PriceAlertsPage, RotationPage, ToolsPage, WatchlistPage } from './latePages'
import { SettingsPage } from './screens/settings'
import { useBootstrapQuery } from './features/queries'
import { reconcileAccent, reconcileAppearance, subscribeSystemAppearance, type Appearance, isAppearance } from './features/appearance'
import { TOOL_CATALOG } from './features/toolsCatalog'
import { accountMonthYear } from './features/accountTime'
import { MonthlyReviewPage, MonthlyReviewRedirect } from './MonthlyReviewPage'
import { CockpitProvider, SectionError, type Page } from './shell'
import { isLocale, reconcileLocale, useI18n } from './i18n'
import { LandingPage } from './LandingPage'

const PATHS: Record<Page, string> = {
  today: '/today', diary: '/diary', calendar: '/calendar', discipline: '/discipline', alerts: '/alerts',
  more: '/more', review: '/review', settings: '/settings', watchlist: '/watchlist', 'price-alerts': '/price-alerts', rotation: '/rotation', partners: '/partners',
  articles: '/articles', tools: '/tools',
}
const NAV: { id: Page; labelKey: 'nav.today' | 'nav.diary' | 'nav.calendar' | 'nav.discipline' | 'nav.alerts'; icon: Parameters<typeof Icon>[0]['name'] }[] = [
  { id: 'today', labelKey: 'nav.today', icon: 'today' },
  { id: 'diary', labelKey: 'nav.diary', icon: 'diary' },
  { id: 'calendar', labelKey: 'nav.calendar', icon: 'calendar' },
  { id: 'discipline', labelKey: 'nav.discipline', icon: 'compass' },
  { id: 'alerts', labelKey: 'nav.alerts', icon: 'bell' },
]
const MORE: { id: Page; labelKey: 'nav.review' | 'nav.settings' | 'nav.watchlist' | 'nav.priceAlerts' | 'nav.rotation' | 'nav.partners' | 'nav.articles' | 'nav.tools' }[] = [
  { id: 'review', labelKey: 'nav.review' },
  { id: 'settings', labelKey: 'nav.settings' },
  { id: 'watchlist', labelKey: 'nav.watchlist' }, { id: 'price-alerts', labelKey: 'nav.priceAlerts' },
  { id: 'rotation', labelKey: 'nav.rotation' }, { id: 'partners', labelKey: 'nav.partners' },
  { id: 'articles', labelKey: 'nav.articles' }, { id: 'tools', labelKey: 'nav.tools' },
]

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/register" element={<RegisterPage />} />
      <Route path="/" element={<RootEntry />} />
      <Route path="/tools" element={<ToolsEntry />} />
      <Route element={<RequireAuth />}>
        <Route element={<Shell />}>
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
          <Route path="/partners/:partnerId/compare" element={<PartnerComparePage />} />
          <Route path="/articles" element={<ArticlesPage />} />
          <Route path="/articles/:slug" element={<ArticleDetailPage />} />
          <Route path="*" element={<NotFoundPage />} />
        </Route>
      </Route>
    </Routes>
  )
}

function RootEntry() {
  const { state } = useAuth()
  if (state === 'restoring') return null
  if (state === 'authenticated') return <Navigate to="/today" replace />
  return <PublicShell><LandingPage /></PublicShell>
}

function ToolsEntry() {
  const { state } = useAuth()
  if (state === 'restoring') return null
  if (state === 'authenticated') return <Shell><ToolsPage /></Shell>
  return <PublicShell><ToolsPage /></PublicShell>
}

function PublicShell({ children }: { children: ReactNode }) {
  const { t, locale, setLocale } = useI18n()
  const [menuOpen, setMenuOpen] = useState(false)
  const [toolsOpen, setToolsOpen] = useState(false)
  const [toolsFocus, setToolsFocus] = useState(0)
  const toolsWrapRef = useRef<HTMLDivElement>(null)
  const toolsBtnRef = useRef<HTMLButtonElement>(null)
  const menuBtnRef = useRef<HTMLButtonElement>(null)
  const menuFirstRef = useRef<HTMLAnchorElement>(null)
  const itemRefs = useRef<Array<HTMLAnchorElement | null>>([])
  const menuId = useId()
  const toolsMenuId = useId()
  const location = useLocation()

  useEffect(() => {
    setMenuOpen(false)
    setToolsOpen(false)
  }, [location.pathname, location.search])

  useEffect(() => {
    if (!toolsOpen) return
    const onDoc = (e: MouseEvent) => {
      if (toolsWrapRef.current && !toolsWrapRef.current.contains(e.target as Node)) setToolsOpen(false)
    }
    document.addEventListener('mousedown', onDoc)
    return () => document.removeEventListener('mousedown', onDoc)
  }, [toolsOpen])

  useEffect(() => {
    if (!toolsOpen) return
    itemRefs.current[toolsFocus]?.focus()
  }, [toolsOpen, toolsFocus])

  useEffect(() => {
    if (!menuOpen) return
    menuFirstRef.current?.focus()
    const onKey = (e: globalThis.KeyboardEvent) => {
      if (e.key === 'Escape') {
        setMenuOpen(false)
        menuBtnRef.current?.focus()
      }
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [menuOpen])

  function closeTools(restore = false) {
    setToolsOpen(false)
    if (restore) toolsBtnRef.current?.focus()
  }

  function onToolsKeyDown(e: ReactKeyboardEvent) {
    if (!toolsOpen) {
      if (e.key === 'ArrowDown' || e.key === 'Enter' || e.key === ' ') {
        e.preventDefault()
        setToolsOpen(true)
        setToolsFocus(0)
      }
      return
    }
    const last = TOOL_CATALOG.length - 1
    if (e.key === 'Escape') {
      e.preventDefault()
      closeTools(true)
    } else if (e.key === 'ArrowDown') {
      e.preventDefault()
      setToolsFocus(i => (i >= last ? 0 : i + 1))
    } else if (e.key === 'ArrowUp') {
      e.preventDefault()
      setToolsFocus(i => (i <= 0 ? last : i - 1))
    } else if (e.key === 'Home') {
      e.preventDefault()
      setToolsFocus(0)
    } else if (e.key === 'End') {
      e.preventDefault()
      setToolsFocus(last)
    }
  }

  const lang = (
    <div className="lang-toggle" role="group" aria-label={t('settings.language')}>
      <button type="button" className={locale === 'en' ? 'is-active' : undefined} onClick={() => { void setLocale('en') }}>EN</button>
      <button type="button" className={locale === 'zh-Hant' ? 'is-active' : undefined} onClick={() => { void setLocale('zh-Hant') }}>繁</button>
    </div>
  )

  const toolsDropdown = (
    <div className="public-shell__tools" ref={toolsWrapRef} onKeyDown={onToolsKeyDown}>
      <button
        ref={toolsBtnRef}
        type="button"
        className={cx('public-shell__tools-btn', toolsOpen && 'is-open')}
        aria-expanded={toolsOpen}
        aria-controls={toolsMenuId}
        aria-haspopup="menu"
        onClick={() => {
          setToolsOpen(v => {
            const next = !v
            if (next) setToolsFocus(0)
            return next
          })
        }}
      >
        {t('landing.nav.toolsMenu')}
      </button>
      {toolsOpen ? (
        <div id={toolsMenuId} className="public-shell__tools-menu" role="menu">
          {TOOL_CATALOG.map((item, index) => (
            <Link
              key={item.id}
              role="menuitem"
              ref={el => { itemRefs.current[index] = el }}
              to={item.href}
              tabIndex={index === toolsFocus ? 0 : -1}
              onClick={() => closeTools(false)}
              onFocus={() => setToolsFocus(index)}
            >
              <Icon name={item.icon} size={16} />
              <span className="public-shell__tools-copy">
                <strong>{t(item.labelKey)}</strong>
                <span>{t(item.bodyKey)}</span>
              </span>
            </Link>
          ))}
        </div>
      ) : null}
    </div>
  )

  return (
    <div className="public-shell">
      <header className="public-shell__top">
        <Link to="/" className="public-shell__brand" aria-label={t('brand.name')}>
          <Brand />
        </Link>
        <nav className="public-shell__nav public-shell__nav--desktop" aria-label={t('landing.nav.label')}>
          <a href="/#product">{t('landing.nav.product')}</a>
          <a href="/#features">{t('landing.nav.features')}</a>
          {toolsDropdown}
          <Link to="/login">{t('landing.cta.signIn')}</Link>
          <Link className="btn btn--primary btn--sm" to="/register"><span className="btn__label">{t('landing.cta.register')}</span></Link>
          {lang}
          <ThemeControls compact />
        </nav>
        <div className="public-shell__mobile-actions">
          <ThemeToggle />
          <button
            ref={menuBtnRef}
            type="button"
            className="public-shell__menu-btn"
            aria-expanded={menuOpen}
            aria-controls={menuId}
            aria-label={menuOpen ? t('landing.nav.close') : t('landing.nav.menu')}
            onClick={() => setMenuOpen(v => !v)}
          >
            <Icon name="layers" size={20} />
          </button>
        </div>
      </header>
      {menuOpen ? (
        <div id={menuId} className="public-shell__drawer">
          <nav className="public-shell__drawer-nav" aria-label={t('landing.nav.label')}>
            <a ref={menuFirstRef} href="/#product" onClick={() => setMenuOpen(false)}>{t('landing.nav.product')}</a>
            <a href="/#features" onClick={() => setMenuOpen(false)}>{t('landing.nav.features')}</a>
            <p className="public-shell__drawer-label">{t('landing.nav.toolsMenu')}</p>
            {TOOL_CATALOG.map(item => (
              <Link key={item.id} className="public-shell__drawer-tool" to={item.href} onClick={() => setMenuOpen(false)}>
                <Icon name={item.icon} size={16} />
                <span>
                  <strong>{t(item.labelKey)}</strong>
                  <span>{t(item.bodyKey)}</span>
                </span>
              </Link>
            ))}
            <hr className="public-shell__drawer-rule" />
            <Link to="/login" onClick={() => setMenuOpen(false)}>{t('landing.cta.signIn')}</Link>
            <Link className="btn btn--primary btn--sm" to="/register" onClick={() => setMenuOpen(false)}>
              <span className="btn__label">{t('landing.cta.register')}</span>
            </Link>
            <div className="public-shell__drawer-row">{lang}</div>
            <div className="public-shell__drawer-row">
              <ThemeControls />
            </div>
          </nav>
        </div>
      ) : null}
      <main className="public-shell__main" id="content">{children}</main>
    </div>
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

function Shell({ children }: { children?: ReactNode }) {
  const navigate = useNavigate()
  const { logout } = useAuth()
  const { confirm, confirmNode } = useConfirm()
  const { t } = useI18n()
  const bootstrap = useBootstrapQuery()
  useEffect(() => {
    if (!bootstrap.data) return
    const appearance = isAppearance(bootstrap.data.appearance) ? bootstrap.data.appearance as Appearance : 'system'
    reconcileAppearance(appearance)
    reconcileAccent(bootstrap.data.accentTheme)
    if (isLocale(bootstrap.data.locale)) reconcileLocale(bootstrap.data.locale)
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
      <a className="skip-link" href="#content">{t('common.skipToContent')}</a>
      <div className="shell">
        <Sidebar cockpit={bootstrap.data} onSignOut={logout} />
        <div className="main">
          <MobileTop cockpit={bootstrap.data} onSignOut={logout} />
          <main className="content" id="content">{children ?? <Outlet />}</main>
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
  const { t } = useI18n()
  const moreOpen = MORE.some(item => location.pathname.startsWith(PATHS[item.id]))
  return (
    <aside className="sidebar" aria-label="Primary">
      <Brand />
      <div className="sidebar__body">
        <div className="sidebar__label">{t('nav.reflect')}</div>
        <nav className="nav" aria-label="Primary sections">
          {NAV.map(item => <NavItem key={item.id} item={item} />)}
        </nav>
        <details className="more-nav" open={moreOpen}>
          <summary>{t('nav.decisionSupport')}</summary>
          <div className="more-nav__list">
            {MORE.map(item => (
              <NavLink key={item.id} className={({ isActive }) => cx('nav__item', isActive && 'is-active')} to={PATHS[item.id]}>
                {t(item.labelKey)}
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
          <ThemeControls compact />
          <Button variant="ghost" icon="logout" onClick={onSignOut} className="signout-btn">{t('common.signOut')}</Button>
        </div>
      </div>
    </aside>
  )
}

function NavItem({ item }: { item: (typeof NAV)[number] }) {
  const { t } = useI18n()
  return <NavLink to={PATHS[item.id]} className={({ isActive }) => cx('nav__item', isActive && 'is-active')}><Icon name={item.icon} size={18} /><span>{t(item.labelKey)}</span></NavLink>
}

function MobileTop({ cockpit, onSignOut }: { cockpit: BootstrapData; onSignOut: () => void }) {
  const { t } = useI18n()
  return (
    <header className="mobile-top">
      <Brand compact />
      <span className="mobile-top__meta">{cockpit.currentLocalDate}</span>
      <div className="mobile-top__actions">
        <ThemeControls compact />
        <IconButton icon="logout" label={t('common.signOut')} onClick={onSignOut} />
      </div>
    </header>
  )
}
function MobileNav() {
  const mobile = NAV.slice(0, 4)
  const location = useLocation()
  const { t } = useI18n()
  const moreActive = location.pathname.startsWith('/more') || location.pathname.startsWith('/alerts') || MORE.some(item => location.pathname.startsWith(PATHS[item.id]))
  return <nav className="mobile-nav" aria-label="Sections">{mobile.map(item => <NavLink key={item.id} to={PATHS[item.id]} className={({ isActive }) => cx('mobile-nav__item', isActive && 'is-active')}><Icon name={item.icon} size={20} /><span>{t(item.labelKey)}</span></NavLink>)}<NavLink to="/more" className={cx('mobile-nav__item', moreActive && 'is-active')}><Icon name="layers" size={20} /><span>{t('common.more')}</span></NavLink></nav>
}

function NotFoundPage() {
  const { t } = useI18n()
  return <ErrorBox message={t('common.pageNotFound')} />
}
