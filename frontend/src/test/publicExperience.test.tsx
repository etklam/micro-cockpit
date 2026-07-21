import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { BrowserRouter } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import App from '../App'
import { AuthProvider } from '../auth/AuthProvider'
import { I18nProvider } from '../i18n'
import { THEME_PRESETS } from '../features/appearance'
import { TOOL_CATALOG, toolHref } from '../features/toolsCatalog'
import { server } from './setup'

function renderPublic(path = '/') {
  window.history.replaceState({}, '', path)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  server.use(http.post('/api/auth/refresh', () => new HttpResponse(null, { status: 401 })))
  render(
    <QueryClientProvider client={client}>
      <BrowserRouter>
        <AuthProvider>
          <I18nProvider>
            <App />
          </I18nProvider>
        </AuthProvider>
      </BrowserRouter>
    </QueryClientProvider>,
  )
}

describe('public experience', () => {
  it('renders landing sections, preview, trust, and free-tool links', async () => {
    renderPublic('/')
    expect(await screen.findByRole('heading', { name: 'A quiet cockpit for reflection.' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'How it works' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'What it is for' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'What you get' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Tools you can use without signing in' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Built for honest notes' })).toBeInTheDocument()
    expect(screen.getByLabelText('Preview')).toBeInTheDocument()
    expect(screen.getByText('Quick note')).toBeInTheDocument()
    expect(screen.getByText('Daily P/L')).toBeInTheDocument()
    expect(screen.getByText('No brokerage execution or order routing')).toBeInTheDocument()
    expect(screen.getByText('Decision support, not investment advice')).toBeInTheDocument()
    for (const tool of TOOL_CATALOG) {
      const link = screen.getAllByRole('link').find(a => a.getAttribute('href') === toolHref(tool.id))
      expect(link).toBeTruthy()
    }
    expect(screen.getAllByText('Free tool').length).toBe(TOOL_CATALOG.length)
  })

  it('exposes product/features anchors and tools dropdown content', async () => {
    renderPublic('/')
    await screen.findByRole('heading', { name: 'A quiet cockpit for reflection.' })
    const desktopNav = screen.getByRole('navigation', { name: 'Public' })
    expect(within(desktopNav).getByRole('link', { name: 'Product' })).toHaveAttribute('href', '/#product')
    expect(within(desktopNav).getByRole('link', { name: 'Features' })).toHaveAttribute('href', '/#features')
    await userEvent.click(within(desktopNav).getByRole('button', { name: 'Tools' }))
    const menu = await screen.findByRole('menu')
    expect(within(menu).getAllByRole('menuitem')).toHaveLength(TOOL_CATALOG.length)
    expect(within(menu).getByRole('menuitem', { name: /Position size/i })).toHaveAttribute('href', '/tools?tool=position-sizing')
    await userEvent.keyboard('{Escape}')
    expect(screen.queryByRole('menu')).not.toBeInTheDocument()
  })

  it('supports arrow-key focus in tools dropdown', async () => {
    renderPublic('/')
    await screen.findByRole('heading', { name: 'A quiet cockpit for reflection.' })
    const toolsBtn = screen.getByRole('button', { name: 'Tools' })
    toolsBtn.focus()
    await userEvent.keyboard('{ArrowDown}')
    const menu = await screen.findByRole('menu')
    const items = within(menu).getAllByRole('menuitem')
    expect(items[0]).toHaveFocus()
    await userEvent.keyboard('{ArrowDown}')
    expect(items[1]).toHaveFocus()
    await userEvent.keyboard('{Escape}')
    expect(toolsBtn).toHaveFocus()
  })

  it('opens mobile menu with product, tools, language, and four themes', async () => {
    // Force mobile drawer path: open menu button is always in DOM for mobile actions CSS, but still in document
    renderPublic('/')
    await screen.findByRole('heading', { name: 'A quiet cockpit for reflection.' })
    const menuButton = screen.getByRole('button', { name: 'Menu' })
    await userEvent.click(menuButton)
    const drawer = document.getElementById(menuButton.getAttribute('aria-controls') ?? '')
    expect(drawer).not.toBeNull()
    const drawerEl = drawer!
    expect(within(drawerEl).getByRole('navigation', { name: 'Public' })).toBeInTheDocument()
    expect(within(drawerEl).getByRole('link', { name: 'Product' })).toHaveFocus()
    expect(within(drawerEl).getByRole('link', { name: 'Product' })).toBeInTheDocument()
    expect(within(drawerEl).getByRole('link', { name: 'Features' })).toBeInTheDocument()
    expect(within(drawerEl).getByText('Risk amount and share quantity from account, risk %, entry, and stop.')).toBeInTheDocument()
    expect(within(drawerEl).getByRole('link', { name: 'Sign in' })).toBeInTheDocument()
    expect(within(drawerEl).getByRole('link', { name: 'Create account' })).toBeInTheDocument()
    const themeGroup = within(drawerEl).getByRole('radiogroup', { name: 'Theme presets' })
    expect(within(themeGroup).getAllByRole('radio')).toHaveLength(THEME_PRESETS.length)
    expect(THEME_PRESETS).toHaveLength(4)
  })

  it('falls back unknown tool query to position-sizing', async () => {
    renderPublic('/tools?tool=not-a-tool')
    expect(await screen.findByRole('heading', { name: 'Tools' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Position size' })).toBeInTheDocument()
    expect(window.location.search).toBe('?tool=position-sizing')
  })

  it('switches public copy to Traditional Chinese', async () => {
    renderPublic('/')
    await screen.findByRole('heading', { name: 'A quiet cockpit for reflection.' })
    const lang = screen.getAllByRole('group', { name: 'Language' })[0]
    await userEvent.click(within(lang).getByRole('button', { name: '繁' }))
    expect(await screen.findByRole('heading', { name: '安靜的反思駕駛艙。' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: '使用方式' })).toBeInTheDocument()
  })
})
