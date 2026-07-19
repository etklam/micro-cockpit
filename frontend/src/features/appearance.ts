export type Appearance = 'system' | 'light' | 'dark'
export type ColorScheme = 'light' | 'dark'

const STORAGE_KEY = 'td_appearance'
const ROOT_ATTR = 'data-theme'

export function isAppearance(value: unknown): value is Appearance {
  return value === 'system' || value === 'light' || value === 'dark'
}

export function readAppearanceMirror(): Appearance | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    return isAppearance(raw) ? raw : null
  } catch {
    return null
  }
}

export function writeAppearanceMirror(value: Appearance) {
  try { localStorage.setItem(STORAGE_KEY, value) } catch { /* private mode */ }
}

export function resolveAppearance(preference: Appearance): ColorScheme {
  if (preference === 'light' || preference === 'dark') return preference
  return window.matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark'
}

export function readDocumentScheme(): ColorScheme {
  return document.documentElement.getAttribute(ROOT_ATTR) === 'light' ? 'light' : 'dark'
}

/** Apply preference to <html data-theme="light|dark">. system follows OS. */
export function applyAppearance(preference: Appearance) {
  const scheme = resolveAppearance(preference)
  document.documentElement.setAttribute(ROOT_ATTR, scheme)
  document.documentElement.style.colorScheme = scheme
  const meta = document.querySelector('meta[name="color-scheme"]')
  if (meta) meta.setAttribute('content', scheme)
  const themeColor = document.querySelector('meta[name="theme-color"]')
  if (themeColor) themeColor.setAttribute('content', scheme === 'light' ? '#f5f5f4' : '#121110')
  return scheme
}

/** Local preference: mirror + paint. */
export function setAppearancePreference(preference: Appearance): ColorScheme {
  writeAppearanceMirror(preference)
  return applyAppearance(preference)
}

/** Flip between explicit light/dark (drops system). */
export function toggleLightDark(preference: Appearance = readAppearanceMirror() ?? 'system'): Appearance {
  const next: Appearance = resolveAppearance(preference) === 'dark' ? 'light' : 'dark'
  setAppearancePreference(next)
  return next
}

/** First-paint: mirror before React. Server preference wins after bootstrap. */
export function bootAppearanceFromMirror() {
  const mirrored = readAppearanceMirror() ?? 'system'
  applyAppearance(mirrored)
}

/**
 * Reconcile local mirror with authenticated server preference.
 * Server wins; updates mirror so next cold start matches.
 */
export function reconcileAppearance(server: Appearance | null | undefined) {
  if (!isAppearance(server)) return
  setAppearancePreference(server)
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
 * Logout policy: keep last appearance on the login page (no account data —
 * only the enum string). Mirror is not cleared.
 */
export function appearanceOnLogout() {
  const mirrored = readAppearanceMirror() ?? 'system'
  applyAppearance(mirrored)
}
