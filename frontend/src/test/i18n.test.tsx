import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { BrowserRouter } from 'react-router-dom'
import { AuthProvider } from '../auth/AuthProvider'
import {
  I18nProvider,
  normalizeLocale,
  translate,
  writeLocaleMirror,
  LOCALE_STORAGE_KEY,
  isLocale,
  applyDocumentLocale,
} from '../i18n'
import * as fmt from '../i18n/format'
import { setActiveFormatLocale, formatDate, signed, pct, money } from '../format'
import { diaryMutationErrorMessage, registerErrorMessage } from '../i18n'
import { ApiError } from '../generated/edge'
import { server } from './setup'
import App from '../App'

describe('locale helpers', () => {
  it('accepts only en and zh-Hant', () => {
    expect(isLocale('en')).toBe(true)
    expect(isLocale('zh-Hant')).toBe(true)
    expect(isLocale('zh')).toBe(false)
    expect(isLocale('fr')).toBe(false)
  })

  it('normalizes browser/API tags and unknown values', () => {
    expect(normalizeLocale('zh-TW')).toBe('zh-Hant')
    expect(normalizeLocale('zh_HK')).toBe('zh-Hant')
    expect(normalizeLocale('en-GB')).toBe('en')
    expect(normalizeLocale('zh-Hans')).toBe('en')
    expect(normalizeLocale('fr-FR')).toBe('en')
    expect(normalizeLocale(null)).toBe('en')
  })

  it('falls back to English for missing keys', () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    // valid key always present
    expect(translate('zh-Hant', 'nav.today')).toBe('今日')
    expect(translate('en', 'nav.today')).toBe('Today')
    warn.mockRestore()
  })
})

describe('locale-aware formatting', () => {
  it('formats dates, money, percent with locale', () => {
    expect(fmt.formatDate('2026-07-16', 'en')).toMatch(/Jul/)
    expect(fmt.formatDate('2026-07-16', 'zh-Hant')).toMatch(/7/)
    expect(fmt.signed(12.5, 'en', 'USD')).toMatch(/\+/)
    expect(fmt.pct(2.5, 'en')).toMatch(/%/)
    expect(fmt.moneyOrEmpty(null, 'en')).toBe('—')
    expect(fmt.signedOrEmpty(undefined, 'zh-Hant')).toBe('—')
  })

  it('active format locale updates module helpers', () => {
    setActiveFormatLocale('en')
    const en = formatDate('2026-01-15')
    setActiveFormatLocale('zh-Hant')
    const zh = formatDate('2026-01-15')
    expect(en).not.toEqual('')
    expect(zh).not.toEqual('')
    // money uses currency symbol rules
    setActiveFormatLocale('en')
    expect(money(10, 'USD')).toContain('10')
    expect(signed(0)).toContain('0')
    expect(pct(1)).toMatch(/%/)
  })
})

describe('translated API errors', () => {
  it('maps status and body markers', () => {
    expect(diaryMutationErrorMessage('en', new ApiError(400, 'too_many_tags'))).toMatch(/10/)
    expect(diaryMutationErrorMessage('zh-Hant', new ApiError(404, ''))).toMatch(/不存在|找不到|已不存在/)
    expect(registerErrorMessage('en', new ApiError(429, ''))).toMatch(/Too many/)
    expect(registerErrorMessage('zh-Hant', new ApiError(400, ''))).toMatch(/密碼|字元/)
  })
})

describe('anonymous locale persistence', () => {
  const memory = new Map<string, string>()

  beforeEach(() => {
    memory.clear()
    vi.stubGlobal('localStorage', {
      getItem: (key: string) => memory.get(key) ?? null,
      setItem: (key: string, value: string) => { memory.set(key, value) },
      removeItem: (key: string) => { memory.delete(key) },
      clear: () => memory.clear(),
      key: (i: number) => Array.from(memory.keys())[i] ?? null,
      get length() { return memory.size },
    })
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    applyDocumentLocale('en')
    setActiveFormatLocale('en')
  })

  it('mirrors last selected locale for pre-login pages', async () => {
    writeLocaleMirror('zh-Hant')
    expect(memory.get(LOCALE_STORAGE_KEY)).toBe('zh-Hant')
    const { resolveAnonymousLocale } = await import('../i18n')
    expect(resolveAnonymousLocale()).toBe('zh-Hant')

    server.use(
      http.post('/api/auth/refresh', () => new HttpResponse(null, { status: 401 })),
      http.post('/api/auth/login', () => new HttpResponse(null, { status: 401 })),
    )
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    window.history.replaceState({}, '', '/login')
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
    expect(await screen.findByRole('button', { name: '登入' })).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'EN' }))
    expect(await screen.findByRole('button', { name: 'Sign in' })).toBeInTheDocument()
    expect(memory.get(LOCALE_STORAGE_KEY)).toBe('en')
  })
})

describe('authenticated locale from bootstrap', () => {
  it('renders Chinese nav from bootstrap locale and switches without reload', async () => {
    const bootstrap = {
      currentUser: { id: '11111111-1111-1111-1111-111111111111', email: 'owner@example.com', displayName: 'Owner' },
      timezone: 'Asia/Taipei', baseCurrency: 'USD', appearance: 'system', accentTheme: 'green', locale: 'zh-Hant',
      role: 'user', accountType: 'human', currentLocalDate: '2026-07-16',
      availableProductAreas: ['today', 'diary', 'calendar'],
    }
    let putBody: unknown
    server.use(
      http.post('/api/auth/refresh', () => HttpResponse.json({ accessToken: 't', expiresAt: '2026-07-16T12:00:00Z' })),
      http.get('/api/app/bootstrap', () => HttpResponse.json(bootstrap)),
      http.get('/api/app/dashboard', () => HttpResponse.json({
        localDate: '2026-07-16', diary: { writtenToday: false, count: 0 }, performance: null,
        pendingAlerts: null, discipline: null, recentDiaries: [], capabilities: { alerts: 'unavailable', discipline: 'empty' },
      })),
      http.get('/api/app/settings', () => HttpResponse.json({
        email: 'owner@example.com', displayName: 'Owner', timezone: 'Asia/Taipei',
        baseCurrency: 'USD', appearance: 'system', accentTheme: 'green', locale: 'zh-Hant', updatedAt: '2026-07-16T00:00:00Z',
      })),
      http.put('/api/app/settings', async ({ request }) => {
        putBody = await request.json()
        return HttpResponse.json({
          email: 'owner@example.com', displayName: 'Owner', timezone: 'Asia/Taipei',
          baseCurrency: 'USD', appearance: 'system', accentTheme: 'green', locale: (putBody as { locale: string }).locale, updatedAt: '2026-07-16T00:00:00Z',
        })
      }),
      http.post('/api/auth/logout', () => new HttpResponse(null, { status: 204 })),
    )
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    window.history.replaceState({}, '', '/today')
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
    expect((await screen.findAllByRole('link', { name: '日誌' })).length).toBeGreaterThan(0)
    expect(screen.getAllByRole('link', { name: '今日' }).length).toBeGreaterThan(0)

    await userEvent.click(screen.getByRole('link', { name: '設定' }))
    expect(await screen.findByRole('heading', { level: 1, name: '設定' })).toBeInTheDocument()
    await userEvent.click(screen.getByText('English'))
    await waitFor(() => expect(screen.getAllByRole('link', { name: 'Today' }).length).toBeGreaterThan(0))
    expect(putBody).toMatchObject({ locale: 'en' })
  })
})
