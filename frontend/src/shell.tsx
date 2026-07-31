import { Component, createContext, useContext } from 'react'
import type { ErrorInfo, ReactNode } from 'react'
import { ErrorBox } from './ui'
import type { ConfirmOpts } from './ui'
import { useI18n } from './i18n'

export type Page =
  | 'today' | 'review' | 'watchlist' | 'calendar' | 'tools' | 'settings'

type Cockpit = { go: (page: Page) => void; confirm: (options: ConfirmOpts) => Promise<boolean> }
const CockpitContext = createContext<Cockpit>(null!)
export const useCockpit = () => useContext(CockpitContext)
export const CockpitProvider = CockpitContext.Provider

type ErrorBoundaryProps = {
  children: ReactNode
  fallback: (reset: () => void) => ReactNode
}

type ErrorBoundaryState = { error: Error | null }

export class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  state: ErrorBoundaryState = { error: null }

  static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    return { error }
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error('Unhandled render error', error, info)
  }

  private reset = () => this.setState({ error: null })

  render() {
    return this.state.error ? this.props.fallback(this.reset) : this.props.children
  }
}

export function AppErrorBoundary({ children }: { children: ReactNode }) {
  const { t } = useI18n()
  return (
    <ErrorBoundary fallback={reset => <ErrorBox message={t('common.somethingWentWrong')} onRetry={reset} />}>
      {children}
    </ErrorBoundary>
  )
}

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
