import type { ReactNode } from 'react'
import type { InstrumentDirectoryItem, ObservationSubjectWrite, ObservationUpdate } from '../features/api'
import { Button, Field, SelectBox, TextInput } from '../ui'
import { useI18n } from '../i18n'

export const PanelLink = ({ children, onClick }: { children: ReactNode; onClick: () => void }) => (
  <Button variant="ghost" size="sm" icon="arrow" onClick={onClick} className="panel__link">{children}</Button>
)

export const WEEKDAYS = [
  'calendar.weekday.sun',
  'calendar.weekday.mon',
  'calendar.weekday.tue',
  'calendar.weekday.wed',
  'calendar.weekday.thu',
  'calendar.weekday.fri',
  'calendar.weekday.sat',
] as const

export type SubjectDraft = {
  type: '' | 'broad_market' | 'sector' | 'theme' | 'instrument'
  name: string
  instrumentId: string
  market: string
  symbol: string
  displayName: string
}

export const emptySubject = (): SubjectDraft => ({ type: '', name: '', instrumentId: '', market: 'US', symbol: '', displayName: '' })
export const subjectDraft = (subject: ObservationUpdate['primarySubject']): SubjectDraft => subject ? {
  type: subject.type as SubjectDraft['type'], name: subject.name ?? '', instrumentId: subject.instrumentId ?? '',
  market: subject.market ?? 'US', symbol: subject.symbol ?? '', displayName: subject.displayName ?? '',
} : emptySubject()
export const subjectWrite = (subject: SubjectDraft): ObservationSubjectWrite | null => {
  if (!subject.type) return null
  if (subject.type !== 'instrument') return { type: subject.type, name: subject.name }
  return { type: 'instrument', instrumentId: subject.market.trim().toUpperCase() === 'US' ? subject.instrumentId || null : null, market: subject.market, symbol: subject.symbol, displayName: subject.displayName }
}
export const subjectLabel = (subject: NonNullable<ObservationUpdate['primarySubject']>) =>
  subject.type === 'instrument' ? `${subject.symbol} · ${subject.displayName}` : subject.name
export const subjectHistoryHref = (subject: NonNullable<ObservationUpdate['primarySubject']>) => {
  const params = new URLSearchParams()
  if (subject.type === 'instrument') {
    if (subject.instrumentId) params.set('instrumentId', subject.instrumentId)
    else { if (subject.market) params.set('market', subject.market); if (subject.symbol) params.set('symbol', subject.symbol) }
  } else { params.set('subjectType', subject.type); if (subject.name) params.set('subject', subject.name) }
  return `/today/observations?${params}`
}

export function DailyCloseEvidence({ subject }: { subject: NonNullable<ObservationUpdate['primarySubject']> }) {
  const { t } = useI18n()
  if (subject.type !== 'instrument') return null
  if (subject.dailyCloseStatus === 'available' && subject.dailyClose)
    return <span> · {t('today.observations.dailyCloseEvidence', {
      date: subject.dailyClose.tradingDate,
      raw: subject.dailyClose.rawClose,
      adjusted: subject.dailyClose.adjustedClose,
    })}</span>
  return <span> · {subject.dailyCloseStatus === 'unsupported'
    ? t('today.observations.dailyCloseUnsupported')
    : t('today.observations.dailyCloseUnavailable')}</span>
}

export function SubjectFields({ subject, onChange, instruments, prefix }: { subject: SubjectDraft; onChange: (subject: SubjectDraft) => void; instruments: InstrumentDirectoryItem[]; prefix: string }) {
  const { t } = useI18n()
  const set = (patch: Partial<SubjectDraft>) => onChange({ ...subject, ...patch })
  return <div className="stack">
    <Field label={t(prefix === 'today.observations.primary' ? 'today.observations.primary.type' : 'today.observations.related.type')}>
      <SelectBox value={subject.type} onChange={event => set({ type: event.target.value as SubjectDraft['type'] })}>
        <option value="">{t('today.observations.subject.none')}</option>
        <option value="broad_market">{t('today.observations.subject.broadMarket')}</option>
        <option value="sector">{t('today.observations.subject.sector')}</option>
        <option value="theme">{t('today.observations.subject.theme')}</option>
        <option value="instrument">{t('today.observations.subject.instrument')}</option>
      </SelectBox>
    </Field>
    {subject.type && subject.type !== 'instrument' ? <Field label={t('today.observations.subjectName')}><TextInput value={subject.name} onChange={event => set({ name: event.target.value })} /></Field> : null}
    {subject.type === 'instrument' ? <>
      <Field label={t('today.observations.market')}><TextInput value={subject.market} onChange={event => set({ market: event.target.value, instrumentId: '', symbol: '', displayName: '' })} /></Field>
      {subject.market.trim().toUpperCase() === 'US' ? <Field label={t('today.observations.usInstrument')}>
        <SelectBox value={subject.instrumentId} onChange={event => {
          const selected = instruments.find(item => item.instrumentId === event.target.value)
          set({ instrumentId: event.target.value, symbol: selected?.symbol ?? '', displayName: selected?.name ?? '' })
        }}>
          <option value="">{t('today.observations.chooseInstrument')}</option>
          {instruments.map(item => <option key={item.instrumentId} value={item.instrumentId}>{item.symbol} · {item.name}</option>)}
        </SelectBox>
      </Field> : <>
        <Field label={t('today.observations.symbol')}><TextInput value={subject.symbol} onChange={event => set({ symbol: event.target.value })} /></Field>
        <Field label={t('today.observations.displayName')}><TextInput value={subject.displayName} onChange={event => set({ displayName: event.target.value })} /></Field>
        <p className="form-hint">{t('today.observations.noDailyClose')}</p>
      </>}
    </> : null}
  </div>
}

export function validCalendarDay(value: string | null, year: number, month: number): boolean {
  if (!value || !/^\d{4}-\d{2}-\d{2}$/.test(value)) return false
  const date = new Date(`${value}T00:00:00Z`)
  return !Number.isNaN(date.getTime()) && date.getUTCFullYear() === year && date.getUTCMonth() + 1 === month && date.toISOString().slice(0, 10) === value
}
