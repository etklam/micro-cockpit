import { useCallback, useEffect, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useAuth } from '../auth/AuthProvider'
import * as api from './api'
import { queryKeys } from './queries'
import {
  isAppearance,
  readAppearanceMirror,
  readDocumentScheme,
  resolveAppearance,
  setAppearancePreference,
  subscribeSystemAppearance,
  type Appearance,
  type ColorScheme,
} from './appearance'
import { isLocale } from '../i18n'

/**
 * Live appearance preference + one-tap light/dark toggle.
 * Paints immediately, mirrors locally, persists to account when signed in.
 */
export function useAppearance() {
  const { state } = useAuth()
  const client = useQueryClient()
  const bootstrap = useQuery({
    queryKey: queryKeys.bootstrap,
    queryFn: api.getBootstrap,
    enabled: state === 'authenticated',
    staleTime: 60_000,
  })
  const [preference, setPreference] = useState<Appearance>(() => readAppearanceMirror() ?? 'system')
  const [scheme, setScheme] = useState<ColorScheme>(() => readDocumentScheme())

  useEffect(() => {
    const server = bootstrap.data?.appearance
    if (!isAppearance(server)) return
    setPreference(server)
    setScheme(resolveAppearance(server))
  }, [bootstrap.data?.appearance])

  useEffect(() => subscribeSystemAppearance(() => preference), [preference])

  useEffect(() => {
    const mq = window.matchMedia('(prefers-color-scheme: light)')
    const onChange = () => {
      if (preference === 'system') setScheme(resolveAppearance('system'))
    }
    mq.addEventListener('change', onChange)
    return () => mq.removeEventListener('change', onChange)
  }, [preference])

  const setAppearance = useCallback(async (next: Appearance) => {
    const resolved = setAppearancePreference(next)
    setPreference(next)
    setScheme(resolved)

    client.setQueryData(queryKeys.bootstrap, (old: api.Bootstrap | undefined) =>
      old ? { ...old, appearance: next } : old,
    )
    client.setQueryData(queryKeys.settings, (old: api.UserSettings | undefined) =>
      old ? { ...old, appearance: next } : old,
    )

    const profile = bootstrap.data
    if (state !== 'authenticated' || !profile) return
    try {
      await api.putSettings({
        displayName: profile.currentUser.displayName,
        timezone: profile.timezone,
        baseCurrency: profile.baseCurrency,
        appearance: next,
        locale: isLocale(profile.locale) ? profile.locale : 'en',
      })
    } catch {
      // Local paint + mirror already applied; server will re-reconcile on next bootstrap.
    }
  }, [bootstrap.data, client, state])

  const toggle = useCallback(() => {
    const next: Appearance = resolveAppearance(preference) === 'dark' ? 'light' : 'dark'
    void setAppearance(next)
    return next
  }, [preference, setAppearance])

  return { preference, scheme, setAppearance, toggle }
}
