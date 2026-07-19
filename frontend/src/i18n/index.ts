/**
 * Lightweight typed i18n — no heavy dependency.
 * Decision: internal provider + stable keys; mirrors appearance persistence pattern.
 * Add a library only if plural/ICU needs grow past simple count/_other.
 */
export { I18nProvider, useI18n, useT, useLocale } from './I18nProvider'
export {
  type Locale,
  LOCALES,
  DEFAULT_LOCALE,
  LOCALE_STORAGE_KEY,
  isLocale,
  normalizeLocale,
  resolveAnonymousLocale,
  bootLocaleFromMirror,
  reconcileLocale,
  localeOnLogout,
  writeLocaleMirror,
  readLocaleMirror,
  applyDocumentLocale,
  INTL_LOCALE,
} from './locale'
export type { MessageKey } from './messages'
export { translate, createTranslator } from './translate'
export * as i18nFormat from './format'
export {
  translateApiError,
  diaryMutationErrorMessage,
  diaryDeleteErrorMessage,
  transactionDeleteErrorMessage,
  transactionUpdateErrorMessage,
  registerErrorMessage,
} from './errors'
