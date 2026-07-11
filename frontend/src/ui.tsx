// Shared UI primitives — the component vocabulary for the whole app.
// Plain CSS classes (see App.css). No CSS-in-JS, no component library.

import { forwardRef, useCallback, useEffect, useId, useRef, useState } from 'react'
import type {
  ButtonHTMLAttributes,
  ComponentPropsWithoutRef,
  InputHTMLAttributes,
  ReactNode,
  SelectHTMLAttributes,
  TextareaHTMLAttributes,
} from 'react'
import { cx } from './format'
import { Icon, type IconName } from './icons'

/* ----------------------------- Spinner ----------------------------- */
export function Spinner({ size = 16, className }: { size?: number; className?: string }) {
  return (
    <span
      className={cx('spinner', className)}
      style={{ width: size, height: size }}
      role="status"
      aria-label="Loading"
    />
  )
}

/* ------------------------------ Button ----------------------------- */
type ButtonVariant = 'primary' | 'subtle' | 'ghost' | 'danger'
type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: ButtonVariant
  size?: 'sm' | 'md'
  icon?: IconName
  loading?: boolean
  block?: boolean
}

export const Button = forwardRef<HTMLButtonElement, ButtonProps>(function Button(
  { variant = 'subtle', size = 'md', icon, loading, block, className, children, disabled, type, ...rest },
  ref,
) {
  const sm = size === 'sm'
  return (
    <button
      ref={ref}
      type={type ?? 'button'}
      disabled={disabled || loading}
      aria-busy={loading || undefined}
      className={cx('btn', `btn--${variant}`, sm && 'btn--sm', block && 'btn--block', loading && 'is-loading', className)}
      {...rest}
    >
      {loading ? <Spinner size={sm ? 13 : 15} /> : icon ? <Icon name={icon} size={sm ? 15 : 17} /> : null}
      {children != null && children !== false ? <span className="btn__label">{children}</span> : null}
    </button>
  )
})

export const IconButton = forwardRef<HTMLButtonElement, { icon: IconName; label: string; size?: number } & ButtonHTMLAttributes<HTMLButtonElement>>(
  function IconButton({ icon, label, size = 18, className, type, ...rest }, ref) {
    return (
      <button ref={ref} type={type ?? 'button'} aria-label={label} title={label} className={cx('icon-btn', className)} {...rest}>
        <Icon name={icon} size={size} />
      </button>
    )
  },
)

/* ------------------------------- Card ------------------------------ */
export function Card({ className, flush, as: Tag = 'div', ...rest }: { flush?: boolean; as?: 'div' | 'section' | 'article' } & ComponentPropsWithoutRef<'div'>) {
  const Comp = Tag as 'div'
  return <Comp className={cx('card', flush && 'card--flush', className)} {...rest} />
}

/* ------------------------------ Field ------------------------------ */
export function Field({
  label,
  hint,
  error,
  className,
  children,
}: {
  label: string
  hint?: string
  error?: string
  className?: string
  children: ReactNode
}) {
  return (
    <label className={cx('field', error && 'field--error', className)}>
      <span className="field__label">{label}</span>
      {children}
      {error ? (
        <span className="field__error" role="alert">{error}</span>
      ) : hint ? (
        <span className="field__hint">{hint}</span>
      ) : null}
    </label>
  )
}

/* --------------------- Form controls (thin wrappers) --------------- */
export const TextInput = forwardRef<HTMLInputElement, InputHTMLAttributes<HTMLInputElement>>(
  function TextInput({ className, ...rest }, ref) {
    return <input ref={ref} className={cx('input', className)} {...rest} />
  },
)
export const TextArea = forwardRef<HTMLTextAreaElement, TextareaHTMLAttributes<HTMLTextAreaElement>>(
  function TextArea({ className, ...rest }, ref) {
    return <textarea ref={ref} className={cx('input', 'textarea', className)} {...rest} />
  },
)
export const SelectBox = forwardRef<HTMLSelectElement, SelectHTMLAttributes<HTMLSelectElement>>(
  function SelectBox({ className, children, ...rest }, ref) {
    return (
      <select ref={ref} className={cx('input', 'select', className)} {...rest}>
        {children}
      </select>
    )
  },
)

/* ------------------------------ Badge ------------------------------ */
type Tone = 'muted' | 'primary' | 'gain' | 'loss' | 'warn'
export function Badge({ tone = 'muted', className, children }: { tone?: Tone; className?: string; children: ReactNode }) {
  return <span className={cx('badge', `badge--${tone}`, className)}>{children}</span>
}

/* ----------------------------- Skeleton ---------------------------- */
export function Skeleton({ className, w, h }: { className?: string; w?: number | string; h?: number | string }) {
  return <div className={cx('skel', className)} style={{ width: w, height: h }} aria-hidden="true" />
}
export function SkeletonText({ lines = 3, className }: { lines?: number; className?: string }) {
  return (
    <div className={cx('skel-lines', className)} aria-hidden="true">
      {Array.from({ length: lines }, (_, i) => (
        <span key={i} className="skel" style={{ width: i === lines - 1 ? '55%' : '100%' }} />
      ))}
    </div>
  )
}

/* --------------------------- State boxes --------------------------- */
export function EmptyBox({
  icon = 'sparkle',
  title,
  hint,
  action,
  className,
  dense,
}: {
  icon?: IconName
  title: string
  hint?: string
  action?: ReactNode
  className?: string
  dense?: boolean
}) {
  return (
    <div className={cx('state-box', 'state-box--empty', dense && 'state-box--dense', className)}>
      <span className="state-box__icon"><Icon name={icon} size={20} /></span>
      <p className="state-box__title">{title}</p>
      {hint ? <p className="state-box__hint">{hint}</p> : null}
      {action}
    </div>
  )
}

export function ErrorBox({ message = 'Something went wrong.', onRetry, className }: { message?: string; onRetry?: () => void; className?: string }) {
  return (
    <div className={cx('state-box', 'state-box--error', className)} role="alert">
      <span className="state-box__icon state-box__icon--error"><Icon name="dot" size={12} /></span>
      <div className="state-box__body">
        <p className="state-box__title">{message}</p>
        {onRetry ? <Button size="sm" variant="ghost" icon="right" onClick={onRetry} className="state-box__retry">Try again</Button> : null}
      </div>
    </div>
  )
}

/* --------------------------- Brand / mark -------------------------- */
export function Gauge({ size = 24, className }: { size?: number; className?: string }) {
  return (
    <svg className={cx('gauge', className)} width={size} height={size} viewBox="0 0 24 24" fill="none" aria-hidden="true">
      <path d="M4 15.5 A 8 8 0 0 1 20 15.5" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" />
      <line x1="12" y1="15.5" x2="17.5" y2="8.5" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" />
      <circle cx="12" cy="15.5" r="1.7" fill="currentColor" />
    </svg>
  )
}

export function Brand({ compact, className }: { compact?: boolean; className?: string }) {
  return (
    <div className={cx('brand', compact && 'brand--compact', className)}>
      <span className="brand__mark"><Gauge size={20} /></span>
      {!compact ? <span className="brand__name">micro cockpit</span> : null}
    </div>
  )
}

/* ---------------------------- PageHeader --------------------------- */
export function PageHeader({
  title,
  subtitle,
  actions,
  className,
}: {
  title: ReactNode
  subtitle?: ReactNode
  actions?: ReactNode
  className?: string
}) {
  return (
    <header className={cx('page-head', className)}>
      <div className="page-head__text">
        <h1>{title}</h1>
        {subtitle ? <p className="page-head__sub">{subtitle}</p> : null}
      </div>
      {actions ? <div className="page-head__actions">{actions}</div> : null}
    </header>
  )
}

/* ------------------------------- Stat ------------------------------ */
export function Stat({
  label,
  value,
  sub,
  tone = 'ink',
  className,
}: {
  label: ReactNode
  value: ReactNode
  sub?: ReactNode
  tone?: 'ink' | 'gain' | 'loss' | 'muted'
  className?: string
}) {
  return (
    <div className={cx('stat', className)}>
      <span className="stat__label">{label}</span>
      <span className={cx('stat__value', 'num', tone !== 'ink' && `is-${tone}`)}>{value}</span>
      {sub ? <span className="stat__sub">{sub}</span> : null}
    </div>
  )
}

/* --------------------------- Confirm dialog ------------------------ */
export type ConfirmOpts = { title: string; message?: string; confirmText?: string; tone?: 'danger' | 'default' }

export function useConfirm() {
  const [state, setState] = useState<{ opts: ConfirmOpts; resolve: (v: boolean) => void } | null>(null)
  const confirmBtnRef = useRef<HTMLButtonElement>(null)
  const headingId = useId()

  const confirm = useCallback(
    (opts: ConfirmOpts) => new Promise<boolean>((resolve) => setState({ opts, resolve })),
    [],
  )

  const close = useCallback((v: boolean) => {
    setState((s) => {
      s?.resolve(v)
      return null
    })
  }, [])

  useEffect(() => {
    if (!state) return
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') { e.preventDefault(); close(false); return }
      if (e.key === 'Tab') {
        const panel = document.querySelector('.confirm__panel')
        const f = panel
          ? Array.from(panel.querySelectorAll<HTMLElement>('button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])')).filter((el) => !el.hasAttribute('disabled'))
          : []
        if (f.length === 0) return
        const first = f[0]
        const last = f[f.length - 1]
        if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus() }
        else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus() }
      }
    }
    window.addEventListener('keydown', onKey)
    confirmBtnRef.current?.focus()
    return () => window.removeEventListener('keydown', onKey)
  }, [state, close])

  const open = !!state
  const tone = state?.opts.tone ?? 'default'

  const node = (
    <div className={cx('confirm', open && 'is-open')} aria-hidden={!open}>
      {state ? (
        <>
          <div className="confirm__backdrop" onClick={() => close(false)} />
          <div
            className="confirm__panel"
            role="alertdialog"
            aria-modal="true"
            aria-labelledby={headingId}
          >
            <h2 id={headingId} className="confirm__title">{state.opts.title}</h2>
            {state.opts.message ? <p className="confirm__msg">{state.opts.message}</p> : null}
            <div className="confirm__actions">
              <Button variant="ghost" onClick={() => close(false)}>Cancel</Button>
              <Button
                ref={confirmBtnRef}
                variant={tone === 'danger' ? 'danger' : 'primary'}
                onClick={() => close(true)}
              >
                {state.opts.confirmText ?? 'Confirm'}
              </Button>
            </div>
          </div>
        </>
      ) : null}
    </div>
  )

  return { confirm, confirmNode: node }
}

/* ----------------------------- useAsync ---------------------------- */
// Fetcher is kept in a ref so deps only need the values that should re-fetch.
export function useAsync<T>(fetcher: () => Promise<T>, deps: unknown[]) {
  const [data, setData] = useState<T | undefined>(undefined)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | undefined>(undefined)
  const [tick, setTick] = useState(0)
  const ref = useRef(fetcher)
  ref.current = fetcher

  useEffect(() => {
    let active = true
    setLoading(true)
    setError(undefined)
    ref.current()
      .then((d) => { if (active) { setData(d); setLoading(false) } })
      .catch(() => { if (active) { setError('Could not load this right now.'); setLoading(false) } })
    return () => { active = false }
    // deps + tick drive reloads; fetcher identity intentionally excluded.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, tick])

  return { data, loading, error, reload: () => setTick((t) => t + 1) }
}
