/** Scheme + chrome accent (two axes). Semantic gain/loss never follow accent. */

export type Appearance = 'system' | 'light' | 'dark'
export type ColorScheme = 'light' | 'dark'
export type Accent = 'green' | 'red'

export type ThemePresetId = 'dark-green' | 'dark-red' | 'light-green' | 'light-red'

export type ThemePreset = {
  id: ThemePresetId
  appearance: 'light' | 'dark'
  accent: Accent
}

export const THEME_PRESETS: readonly ThemePreset[] = [
  { id: 'dark-green', appearance: 'dark', accent: 'green' },
  { id: 'dark-red', appearance: 'dark', accent: 'red' },
  { id: 'light-green', appearance: 'light', accent: 'green' },
  { id: 'light-red', appearance: 'light', accent: 'red' },
] as const

export const DEFAULT_ACCENT: Accent = 'green'

const APPEARANCE_KEY = 'td_appearance'
const ACCENT_KEY = 'td_accent'
const ROOT_THEME = 'data-theme'
const ROOT_ACCENT = 'data-accent'
export const THEME_CHANGE_EVENT = 'td-theme-change'

function notifyThemeChange() {
  window.dispatchEvent(new Event(THEME_CHANGE_EVENT))
}

export function isAppearance(value: unknown): value is Appearance {
  return value === 'system' || value === 'light' || value === 'dark'
}

export function isAccent(value: unknown): value is Accent {
  return value === 'green' || value === 'red'
}

/** Accept server accentTheme; invalid/missing → null (callers default green). */
export function normalizeAccent(value: unknown): Accent | null {
  if (typeof value !== 'string') return null
  const v = value.trim().toLowerCase()
  return isAccent(v) ? v : null
}

export function isThemePresetId(value: unknown): value is ThemePresetId {
  return THEME_PRESETS.some(p => p.id === value)
}

export function presetIdFor(scheme: ColorScheme, accent: Accent): ThemePresetId {
  return `${scheme}-${accent}` as ThemePresetId
}

export function readAppearanceMirror(): Appearance | null {
  try {
    const raw = localStorage.getItem(APPEARANCE_KEY)
    return isAppearance(raw) ? raw : null
  } catch {
    return null
  }
}

export function writeAppearanceMirror(value: Appearance) {
  try { localStorage.setItem(APPEARANCE_KEY, value) } catch { /* private mode */ }
}

export function readAccentMirror(): Accent | null {
  try {
    const raw = localStorage.getItem(ACCENT_KEY)
    return isAccent(raw) ? raw : null
  } catch {
    return null
  }
}

export function writeAccentMirror(value: Accent) {
  try { localStorage.setItem(ACCENT_KEY, value) } catch { /* private mode */ }
}

export function resolveAppearance(preference: Appearance): ColorScheme {
  if (preference === 'light' || preference === 'dark') return preference
  return window.matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark'
}

export function readDocumentScheme(): ColorScheme {
  return document.documentElement.getAttribute(ROOT_THEME) === 'light' ? 'light' : 'dark'
}

export function readDocumentAccent(): Accent {
  return document.documentElement.getAttribute(ROOT_ACCENT) === 'red' ? 'red' : 'green'
}

/** Apply scheme to html[data-theme]. system follows OS. */
export function applyAppearance(preference: Appearance) {
  const scheme = resolveAppearance(preference)
  document.documentElement.setAttribute(ROOT_THEME, scheme)
  document.documentElement.style.colorScheme = scheme
  const meta = document.querySelector('meta[name="color-scheme"]')
  if (meta) meta.setAttribute('content', scheme)
  const themeColor = document.querySelector('meta[name="theme-color"]')
  if (themeColor) themeColor.setAttribute('content', scheme === 'light' ? '#f5f5f4' : '#121110')
  return scheme
}

/** Apply chrome accent to html[data-accent]. Does not touch gain/loss tokens. */
export function applyAccent(accent: Accent) {
  document.documentElement.setAttribute(ROOT_ACCENT, accent)
  return accent
}

export function setAppearancePreference(preference: Appearance): ColorScheme {
  writeAppearanceMirror(preference)
  const scheme = applyAppearance(preference)
  notifyThemeChange()
  return scheme
}

export function setAccentPreference(accent: Accent): Accent {
  writeAccentMirror(accent)
  applyAccent(accent)
  notifyThemeChange()
  return accent
}

/** Flip between explicit light/dark (drops system). Accent unchanged. */
export function toggleLightDark(preference: Appearance = readAppearanceMirror() ?? 'system'): Appearance {
  const next: Appearance = resolveAppearance(preference) === 'dark' ? 'light' : 'dark'
  setAppearancePreference(next)
  return next
}

/** Apply one of the four chrome presets (explicit scheme + accent). */
export function setThemePreset(preset: ThemePreset | ThemePresetId) {
  const p = typeof preset === 'string' ? THEME_PRESETS.find(x => x.id === preset) : preset
  if (!p) return
  setAppearancePreference(p.appearance)
  setAccentPreference(p.accent)
}

/** First-paint: mirror before React. Server wins after bootstrap. */
export function bootAppearanceFromMirror() {
  const mirrored = readAppearanceMirror() ?? 'system'
  applyAppearance(mirrored)
  applyAccent(readAccentMirror() ?? DEFAULT_ACCENT)
}

/**
 * Reconcile local mirror with authenticated server preference.
 * Server wins; updates mirror so next cold start matches.
 */
export function reconcileAppearance(server: Appearance | null | undefined) {
  if (!isAppearance(server)) return
  setAppearancePreference(server)
}

export function reconcileAccent(server: unknown) {
  const accent = normalizeAccent(server)
  if (!accent) return
  setAccentPreference(accent)
}

/** Keep system preference in sync with OS while preference is system. */
export function subscribeSystemAppearance(getPreference: () => Appearance): () => void {
  const mq = window.matchMedia('(prefers-color-scheme: light)')
  const onChange = () => {
    if (getPreference() === 'system') applyAppearance('system')
  }
  mq.addEventListener('change', onChange)
  return () => mq.removeEventListener('change', onChange)
}

/**
 * Logout policy: keep last appearance/accent on public surfaces
 * (chrome preference only — no account data).
 */
export function appearanceOnLogout() {
  applyAppearance(readAppearanceMirror() ?? 'system')
  applyAccent(readAccentMirror() ?? DEFAULT_ACCENT)
}
