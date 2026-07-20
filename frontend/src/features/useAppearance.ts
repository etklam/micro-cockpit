import { useCallback, useEffect, useMemo, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useAuth } from '../auth/AuthProvider'
import * as api from './api'
import { queryKeys } from './queries'
import {
  DEFAULT_ACCENT,
  isAppearance,
  normalizeAccent,
  presetIdFor,
  readAccentMirror,
  readAppearanceMirror,
  readDocumentAccent,
  readDocumentScheme,
  resolveAppearance,
  setAccentPreference,
  setAppearancePreference,
  setThemePreset,
  subscribeSystemAppearance,
  type Accent,
  type Appearance,
  type ColorScheme,
  type ThemePreset,
  type ThemePresetId,
  THEME_CHANGE_EVENT,
  THEME_PRESETS,
} from './appearance'
import { queueSettingsWrite, settingsFromBootstrap } from './settingsWrites'

/**
 * Live appearance (scheme) + chrome accent.
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
  const [accent, setAccentState] = useState<Accent>(() => readAccentMirror() ?? readDocumentAccent() ?? DEFAULT_ACCENT)

  useEffect(() => {
    const server = bootstrap.data?.appearance
    if (isAppearance(server)) {
      setPreference(server)
      setScheme(resolveAppearance(server))
    }
    const serverAccent = normalizeAccent(bootstrap.data?.accentTheme)
    if (serverAccent) setAccentState(serverAccent)
  }, [bootstrap.data])

  useEffect(() => {
    const sync = () => {
      setPreference(readAppearanceMirror() ?? 'system')
      setScheme(readDocumentScheme())
      setAccentState(readDocumentAccent())
    }
    window.addEventListener(THEME_CHANGE_EVENT, sync)
    return () => window.removeEventListener(THEME_CHANGE_EVENT, sync)
  }, [])

  useEffect(() => subscribeSystemAppearance(() => preference), [preference])

  useEffect(() => {
    const mq = window.matchMedia('(prefers-color-scheme: light)')
    const onChange = () => {
      if (preference === 'system') setScheme(resolveAppearance('system'))
    }
    mq.addEventListener('change', onChange)
    return () => mq.removeEventListener('change', onChange)
  }, [preference])

  const persistSettings = useCallback(async (
    patch: Partial<Pick<api.UserSettingsWrite, 'appearance' | 'accentTheme'>>,
  ) => {
    if (state !== 'authenticated') return
    try {
      await queueSettingsWrite(() => settingsFromBootstrap(client, patch), client)
    } catch {
      // Local paint + mirror already applied; server re-reconciles on next bootstrap.
    }
  }, [client, state])

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

    await persistSettings({ appearance: next })
  }, [client, persistSettings])

  const setAccent = useCallback(async (next: Accent) => {
    setAccentPreference(next)
    setAccentState(next)

    client.setQueryData(queryKeys.bootstrap, (old: api.Bootstrap | undefined) =>
      old ? { ...old, accentTheme: next } : old,
    )
    client.setQueryData(queryKeys.settings, (old: api.UserSettings | undefined) =>
      old ? { ...old, accentTheme: next } : old,
    )

    await persistSettings({ accentTheme: next })
  }, [client, persistSettings])

  const applyPreset = useCallback(async (preset: ThemePreset | ThemePresetId) => {
    const p = typeof preset === 'string' ? THEME_PRESETS.find(x => x.id === preset) : preset
    if (!p) return
    setThemePreset(p)
    setPreference(p.appearance)
    setScheme(p.appearance)
    setAccentState(p.accent)

    client.setQueryData(queryKeys.bootstrap, (old: api.Bootstrap | undefined) =>
      old ? { ...old, appearance: p.appearance, accentTheme: p.accent } : old,
    )
    client.setQueryData(queryKeys.settings, (old: api.UserSettings | undefined) =>
      old ? { ...old, appearance: p.appearance, accentTheme: p.accent } : old,
    )

    await persistSettings({ appearance: p.appearance, accentTheme: p.accent })
  }, [client, persistSettings])

  const toggle = useCallback(() => {
    const next: Appearance = resolveAppearance(preference) === 'dark' ? 'light' : 'dark'
    void setAppearance(next)
    return next
  }, [preference, setAppearance])

  const activePresetId = useMemo(() => presetIdFor(scheme, accent), [scheme, accent])

  return {
    preference,
    scheme,
    accent,
    activePresetId,
    presets: THEME_PRESETS,
    setAppearance,
    setAccent,
    applyPreset,
    toggle,
  }
}
