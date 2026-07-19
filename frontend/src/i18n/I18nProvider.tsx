import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react'
import type { ReactNode } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useAuth } from '../auth/AuthProvider'
import * as api from '../features/api'
import { queryKeys } from '../features/queries'
import {
  applyDocumentLocale,
  isLocale,
  normalizeLocale,
  readLocaleMirror,
  resolveAnonymousLocale,
  writeLocaleMirror,
  type Locale,
} from './locale'
import { translate, type TranslateVars } from './translate'
import type { MessageKey } from './messages'
import * as fmt from './format'
import { setActiveFormatLocale } from '../format'

type I18nValue = {
  locale: Locale
  t: (key: MessageKey, vars?: TranslateVars) => string
  setLocale: (next: Locale) => Promise<void>
  format: {
    date: (iso: string) => string
    longDate: (iso: string) => string
    month: (year: number, month1: number) => string
    time: (hhmm: string) => string
    dateTime: (iso: string, timeZone?: string) => string
    money: (n: number, currency?: string) => string
    signed: (n: number, currency?: string) => string
    signedCompact: (n: number) => string
    pct: (n: number, digits?: number) => string
    quantity: (n: number) => string
    number: (n: number, options?: Intl.NumberFormatOptions) => string
    empty: string
    signedOrEmpty: (n: number | null | undefined, currency?: string) => string
    pctOrEmpty: (n: number | null | undefined, digits?: number) => string
    moneyOrEmpty: (n: number | null | undefined, currency?: string) => string
    repeatLabel: (mode: string) => string
  }
}

const I18nContext = createContext<I18nValue | null>(null)

export function I18nProvider({ children }: { children: ReactNode }) {
  const { state } = useAuth()
  const client = useQueryClient()
  const [locale, setLocaleState] = useState<Locale>(() => resolveAnonymousLocale())

  // Bootstrap server locale when authenticated.
  useEffect(() => {
    if (state !== 'authenticated') {
      const anon = resolveAnonymousLocale()
      setLocaleState(anon)
      applyDocumentLocale(anon)
      return
    }
    const bootstrap = client.getQueryData<api.Bootstrap>(queryKeys.bootstrap)
    if (bootstrap && isLocale(bootstrap.locale)) {
      setLocaleState(bootstrap.locale)
      writeLocaleMirror(bootstrap.locale)
      applyDocumentLocale(bootstrap.locale)
    }
  }, [state, client])

  // Subscribe to bootstrap query cache updates without forcing a re-fetch loop.
  useEffect(() => {
    if (state !== 'authenticated') return
    const unsub = client.getQueryCache().subscribe(event => {
      if (event.query.queryKey[0] !== 'bootstrap') return
      const data = event.query.state.data as api.Bootstrap | undefined
      if (data && isLocale(data.locale)) {
        const next: Locale = data.locale
        setLocaleState(prev => {
          if (prev === next) return prev
          writeLocaleMirror(next)
          applyDocumentLocale(next)
          return next
        })
      }
    })
    return unsub
  }, [state, client])

  useEffect(() => {
    applyDocumentLocale(locale)
    setActiveFormatLocale(locale)
  }, [locale])

  const setLocale = useCallback(async (next: Locale) => {
    const localeNext = normalizeLocale(next)
    writeLocaleMirror(localeNext)
    applyDocumentLocale(localeNext)
    setLocaleState(localeNext)

    client.setQueryData(queryKeys.bootstrap, (old: api.Bootstrap | undefined) =>
      old ? { ...old, locale: localeNext } : old,
    )
    client.setQueryData(queryKeys.settings, (old: api.UserSettings | undefined) =>
      old ? { ...old, locale: localeNext } : old,
    )

    if (state !== 'authenticated') return
    const profile = client.getQueryData<api.Bootstrap>(queryKeys.bootstrap)
    if (!profile) return
    try {
      await api.putSettings({
        displayName: profile.currentUser.displayName,
        timezone: profile.timezone,
        baseCurrency: profile.baseCurrency,
        appearance: profile.appearance,
        locale: localeNext,
      })
    } catch {
      // Local paint + mirror already applied; next bootstrap reconciles.
    }
  }, [client, state])

  const t = useCallback((key: MessageKey, vars?: TranslateVars) => translate(locale, key, vars), [locale])

  const format = useMemo<I18nValue['format']>(() => ({
    date: iso => fmt.formatDate(iso, locale),
    longDate: iso => fmt.formatLongDate(iso, locale),
    month: (y, m) => fmt.monthLabel(y, m, locale),
    time: hhmm => fmt.formatTime(hhmm, locale),
    dateTime: (iso, tz) => fmt.formatDateTime(iso, locale, tz),
    money: (n, currency) => fmt.money(n, locale, currency),
    signed: (n, currency) => fmt.signed(n, locale, currency),
    signedCompact: n => fmt.signedCompact(n, locale),
    pct: (n, digits) => fmt.pct(n, locale, digits),
    quantity: n => fmt.quantity(n, locale),
    number: (n, options) => fmt.formatNumber(n, locale, options),
    empty: fmt.emptyValue(locale),
    signedOrEmpty: (n, currency) => fmt.signedOrEmpty(n, locale, currency),
    pctOrEmpty: (n, digits) => fmt.pctOrEmpty(n, locale, digits),
    moneyOrEmpty: (n, currency) => fmt.moneyOrEmpty(n, locale, currency),
    repeatLabel: mode => fmt.repeatLabel(mode, locale),
  }), [locale])

  const value = useMemo(() => ({ locale, t, setLocale, format }), [locale, t, setLocale, format])
  return <I18nContext.Provider value={value}>{children}</I18nContext.Provider>
}

export function useI18n(): I18nValue {
  const ctx = useContext(I18nContext)
  if (!ctx) throw new Error('useI18n must be used within I18nProvider')
  return ctx
}

export function useT() {
  return useI18n().t
}

/** Safe locale read when provider may be absent (tests). */
export function useLocale(): Locale {
  const ctx = useContext(I18nContext)
  return ctx?.locale ?? readLocaleMirror() ?? 'en'
}
