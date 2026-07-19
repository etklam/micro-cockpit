import { createContext, useContext } from 'react'
import { ErrorBox } from './ui'
import type { ConfirmOpts } from './ui'
import { useI18n } from './i18n'

export type Page =
  | 'today' | 'diary' | 'calendar' | 'discipline' | 'alerts' | 'more' | 'review' | 'settings'
  | 'watchlist' | 'price-alerts' | 'rotation' | 'partners' | 'articles' | 'tools'

type Cockpit = { go: (page: Page) => void; confirm: (options: ConfirmOpts) => Promise<boolean> }
const CockpitContext = createContext<Cockpit>(null!)
export const useCockpit = () => useContext(CockpitContext)
export const CockpitProvider = CockpitContext.Provider

export function SectionError({ onRetry }: { onRetry: () => void }) {
  const { t } = useI18n()
  return <ErrorBox message={t('common.couldNotReach')} onRetry={onRetry} />
}

export function PageSkeleton({ rows = 3 }: { rows?: number }) {
  return (
    <div className="page-skel">
      <div className="skel" style={{ height: 34, width: 220 }} />
      <div className="card-grid">
        {Array.from({ length: 3 }, (_, index) => (
          <div className="card" key={index}>
            <div className="skel" style={{ height: 12, width: 90 }} />
            <div className="skel" style={{ height: 30, width: 120, marginTop: 16 }} />
          </div>
        ))}
      </div>
      {Array.from({ length: rows }, (_, index) => (
        <div className="skel" key={index} style={{ height: 56, marginTop: 12 }} />
      ))}
    </div>
  )
}
