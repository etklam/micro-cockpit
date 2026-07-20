import type { QueryClient } from '@tanstack/react-query'
import * as api from './api'

let settingsWrite = Promise.resolve<unknown>(undefined)

/** Serialize full-document settings writes so preference controls cannot clobber each other. */
export function queueSettingsWrite(
  body: api.UserSettingsWrite | (() => api.UserSettingsWrite | null),
  client: QueryClient,
): Promise<api.UserSettings | null> {
  const run = async () => {
    const next = typeof body === 'function' ? body() : body
    if (!next) return null
    const settings = await api.putSettings(next)
    client.setQueryData<api.UserSettings>(['settings'], settings)
    client.setQueryData<api.Bootstrap>(['bootstrap'], old => old ? {
      ...old,
      currentUser: { ...old.currentUser, displayName: settings.displayName },
      timezone: settings.timezone,
      baseCurrency: settings.baseCurrency,
      appearance: settings.appearance,
      locale: settings.locale,
      accentTheme: settings.accentTheme,
    } : old)
    return settings
  }
  const result = settingsWrite.then(run, run)
  settingsWrite = result.catch(() => undefined)
  return result
}

export function settingsFromBootstrap(
  client: QueryClient,
  patch: Partial<Pick<api.UserSettingsWrite, 'appearance' | 'locale' | 'accentTheme'>>,
): api.UserSettingsWrite | null {
  const profile = client.getQueryData<api.Bootstrap>(['bootstrap'])
  if (!profile) return null
  return {
    displayName: profile.currentUser.displayName,
    timezone: profile.timezone,
    baseCurrency: profile.baseCurrency,
    appearance: patch.appearance ?? profile.appearance,
    locale: patch.locale ?? profile.locale,
    accentTheme: patch.accentTheme ?? profile.accentTheme,
  }
}
