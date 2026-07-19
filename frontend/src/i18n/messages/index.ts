import { en, type MessageKey, type Messages } from './en'
import { zhHant } from './zh-Hant'
import type { Locale } from '../locale'

export type { MessageKey, Messages }
export const catalogs: Record<Locale, Messages> = {
  en,
  'zh-Hant': zhHant,
}
