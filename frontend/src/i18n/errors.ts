import { ApiError } from '../generated/edge'
import type { MessageKey } from './messages'
import type { Locale } from './locale'
import { translate } from './translate'

/** Map HTTP status / body markers to stable translation keys. */
export function apiErrorKey(error: unknown): MessageKey {
  if (!(error instanceof ApiError)) return 'error.unknown'
  if (error.status === 401 || error.status === 403) return 'error.auth'
  if (error.status === 404) return 'error.notFound'
  if (error.status === 409) return 'error.conflict'
  if (error.status === 429) return 'error.rateLimited'
  if (error.status === 504) return 'error.timeout'
  if (error.status === 503 || error.status === 502) return 'error.unavailable'
  if (error.status === 400 || error.status === 422) return 'error.validation'
  return 'error.unknown'
}

export function translateApiError(locale: Locale, error: unknown): string {
  return translate(locale, apiErrorKey(error))
}

export function registerErrorMessage(locale: Locale, error: unknown): string {
  if (!(error instanceof ApiError)) return translate(locale, 'auth.register.error.unknown')
  if (error.status === 400) return translate(locale, 'auth.register.error.validation')
  if (error.status === 404) return translate(locale, 'auth.register.error.unavailable')
  if (error.status === 409) return translate(locale, 'auth.register.error.conflict')
  if (error.status === 429) return translate(locale, 'auth.register.error.rateLimited')
  return translate(locale, 'auth.register.error.unknown')
}
