import { afterEach, describe, expect, it } from 'vitest'
import {
  applyAppearance,
  isAppearance,
  resolveAppearance,
  setAppearancePreference,
  toggleLightDark,
} from '../features/appearance'

describe('appearance', () => {
  afterEach(() => {
    document.documentElement.removeAttribute('data-theme')
    document.documentElement.style.colorScheme = ''
  })

  it('accepts only system|light|dark', () => {
    expect(isAppearance('dark')).toBe(true)
    expect(isAppearance('light')).toBe(true)
    expect(isAppearance('system')).toBe(true)
    expect(isAppearance('sepia')).toBe(false)
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

  it('toggles explicit light and dark', () => {
    setAppearancePreference('dark')
    expect(toggleLightDark('dark')).toBe('light')
    expect(document.documentElement.getAttribute('data-theme')).toBe('light')
    expect(toggleLightDark('light')).toBe('dark')
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark')
  })

  it('resolves system against media query', () => {
    expect(resolveAppearance('light')).toBe('light')
    expect(resolveAppearance('dark')).toBe('dark')
    expect(['light', 'dark']).toContain(resolveAppearance('system'))
  })
})
