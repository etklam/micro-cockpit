import { afterEach, describe, expect, it } from 'vitest'
import {
  applyAccent,
  applyAppearance,
  isAccent,
  isAppearance,
  normalizeAccent,
  presetIdFor,
  resolveAppearance,
  setAccentPreference,
  setAppearancePreference,
  setThemePreset,
  toggleLightDark,
} from '../features/appearance'

describe('appearance', () => {
  afterEach(() => {
    document.documentElement.removeAttribute('data-theme')
    document.documentElement.removeAttribute('data-accent')
    document.documentElement.style.colorScheme = ''
    try {
      localStorage.removeItem('td_appearance')
      localStorage.removeItem('td_accent')
    } catch { /* */ }
  })

  it('accepts only system|light|dark', () => {
    expect(isAppearance('dark')).toBe(true)
    expect(isAppearance('light')).toBe(true)
    expect(isAppearance('system')).toBe(true)
    expect(isAppearance('sepia')).toBe(false)
  })

  it('accepts only green|red accents', () => {
    expect(isAccent('green')).toBe(true)
    expect(isAccent('red')).toBe(true)
    expect(isAccent('amber')).toBe(false)
  })

  it('normalizes server accents; invalid is null (default green elsewhere)', () => {
    expect(normalizeAccent('green')).toBe('green')
    expect(normalizeAccent('red')).toBe('red')
    expect(normalizeAccent('RED')).toBe('red')
    expect(normalizeAccent('amber')).toBe(null)
    expect(normalizeAccent('violet')).toBe(null)
    expect(normalizeAccent(undefined)).toBe(null)
  })

  it('applies dark tokens to the document root', () => {
    applyAppearance('dark')
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark')
    expect(document.documentElement.style.colorScheme).toBe('dark')
  })

  it('applies light tokens to the document root', () => {
    applyAppearance('light')
    expect(document.documentElement.getAttribute('data-theme')).toBe('light')
    expect(document.documentElement.style.colorScheme).toBe('light')
  })

  it('applies accent without changing scheme', () => {
    applyAppearance('dark')
    applyAccent('red')
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark')
    expect(document.documentElement.getAttribute('data-accent')).toBe('red')
  })

  it('toggles explicit light and dark (scheme only)', () => {
    setAppearancePreference('dark')
    setAccentPreference('red')
    expect(toggleLightDark('dark')).toBe('light')
    expect(document.documentElement.getAttribute('data-theme')).toBe('light')
    expect(document.documentElement.getAttribute('data-accent')).toBe('red')
  })

  it('sets four chrome presets', () => {
    setThemePreset('light-red')
    expect(document.documentElement.getAttribute('data-theme')).toBe('light')
    expect(document.documentElement.getAttribute('data-accent')).toBe('red')
    expect(presetIdFor('light', 'red')).toBe('light-red')
    setThemePreset('dark-green')
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark')
    expect(document.documentElement.getAttribute('data-accent')).toBe('green')
  })

  it('resolves system against media query', () => {
    expect(resolveAppearance('light')).toBe('light')
    expect(resolveAppearance('dark')).toBe('dark')
    expect(['light', 'dark']).toContain(resolveAppearance('system'))
  })
})
