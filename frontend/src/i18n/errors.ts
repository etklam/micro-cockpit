import { ApiError } from '../generated/edge'
import type { MessageKey } from './messages'
import type { Locale } from './locale'
import { translate } from './translate'

/** Map HTTP status / body markers to stable translation keys. */
export function apiErrorKey(error: unknown): MessageKey {
  if (!(error instanceof ApiError)) return 'error.unknown'
  const body = error.responseBody ?? ''
  if (error.status === 401 || error.status === 403) return 'error.auth'
  if (error.status === 404) return 'error.notFound'
  if (error.status === 409) return 'error.conflict'
  if (error.status === 429) return 'error.rateLimited'
  if (error.status === 504) return 'error.timeout'
  if (error.status === 503 || error.status === 502) return 'error.unavailable'
  if (error.status === 400 || error.status === 422) {
    if (body.includes('too_many_tags')) return 'error.diary.tooManyTags'
    if (body.includes('invalid_tag')) return 'error.diary.invalidTag'
    if (body.includes('title_required')) return 'error.diary.titleRequired'
    return 'error.validation'
  }
  return 'error.unknown'
}

export function translateApiError(locale: Locale, error: unknown): string {
  return translate(locale, apiErrorKey(error))
}

export function diaryMutationErrorMessage(locale: Locale, error: unknown): string {
  if (!(error instanceof ApiError)) return translate(locale, 'error.diary.save')
  if (error.status === 400 || error.status === 422) {
    if (error.responseBody.includes('too_many_tags')) return translate(locale, 'error.diary.tooManyTags')
    if (error.responseBody.includes('invalid_tag')) return translate(locale, 'error.diary.invalidTag')
    if (error.responseBody.includes('title_required')) return translate(locale, 'error.diary.titleRequired')
    return translate(locale, 'error.diary.validation')
  }
  if (error.status === 404) return translate(locale, 'error.diary.notFound')
  if (error.status === 409) return translate(locale, 'error.diary.conflict')
  if (error.status === 503 || error.status === 504) return translate(locale, 'error.diary.unavailable')
  return translate(locale, 'error.diary.save')
}

export function diaryDeleteErrorMessage(locale: Locale, error: unknown): string {
  if (!(error instanceof ApiError)) return translate(locale, 'error.diary.delete')
  if (error.status === 404) return translate(locale, 'error.diary.notFound')
  if (error.status === 503 || error.status === 504) return translate(locale, 'error.diary.unavailable')
  return translate(locale, 'error.diary.delete')
}

export function transactionDeleteErrorMessage(locale: Locale, error: unknown): string {
  if (!(error instanceof ApiError)) return translate(locale, 'error.trade.delete')
  if (error.status === 404) return translate(locale, 'error.trade.notFound')
  if (error.status === 503 || error.status === 504) return translate(locale, 'error.trade.unavailable')
  return translate(locale, 'error.trade.delete')
}

export function transactionUpdateErrorMessage(locale: Locale, error: unknown): string {
  if (!(error instanceof ApiError)) return translate(locale, 'error.trade.save')
  if (error.status === 400 || error.status === 422) return translate(locale, 'error.trade.validation')
  if (error.status === 404) return translate(locale, 'error.trade.notFound')
  if (error.status === 503 || error.status === 504) return translate(locale, 'error.trade.unavailable')
  return translate(locale, 'error.trade.save')
}

export function registerErrorMessage(locale: Locale, error: unknown): string {
  if (!(error instanceof ApiError)) return translate(locale, 'auth.register.error.unknown')
  if (error.status === 400) return translate(locale, 'auth.register.error.validation')
  if (error.status === 404) return translate(locale, 'auth.register.error.unavailable')
  if (error.status === 409) return translate(locale, 'auth.register.error.conflict')
  if (error.status === 429) return translate(locale, 'auth.register.error.rateLimited')
  return translate(locale, 'auth.register.error.unknown')
}
