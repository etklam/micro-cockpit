import { expect, test } from 'vitest'
import apiSource from '../features/api.ts?raw'
import pageSource from '../latePages.tsx?raw'

test('rotation page uses the feature boundary without raw transport access', () => {
  expect(pageSource).not.toMatch(/\bfetch\s*\(/)
  expect(pageSource).not.toContain('/api/app/rotation')
  expect(pageSource).not.toContain('/internal/rotation')
  expect(pageSource).not.toContain('api/generated')
  expect(apiSource).not.toMatch(/https?:\/\/(localhost|127\.0\.0\.1)/)
})
