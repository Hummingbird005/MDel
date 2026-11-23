import { Loader2, X } from 'lucide-react'
import { useEffect, type ButtonHTMLAttributes, type ReactNode } from 'react'
import { cx, initials } from '../../lib/cx'

type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger' | 'success'

export function Button({
  variant = 'primary',
  size = 'md',
  loading,
  className,
  children,
  disabled,
  ...props
}: ButtonHTMLAttributes<HTMLButtonElement> & { variant?: ButtonVariant; size?: 'sm' | 'md'; loading?: boolean }) {
  const variants: Record<ButtonVariant, string> = {
    primary: 'bg-accent text-accent-fg shadow-sm hover:bg-accent/90',
    secondary: 'bg-card text-fg ring-1 ring-inset ring-line hover:bg-subtle',
    ghost: 'text-muted hover:bg-subtle hover:text-fg',
    danger: 'bg-danger text-white shadow-sm hover:bg-danger/90',
    success: 'bg-success text-white shadow-sm hover:bg-success/90',
  }
  return (
    <button
      {...props}
      disabled={disabled || loading}
      className={cx(
        'inline-flex items-center justify-center gap-2 whitespace-nowrap rounded-lg font-medium transition-colors disabled:cursor-not-allowed disabled:opacity-50',
        size === 'sm' ? 'h-8 px-2.5 text-xs' : 'h-9 px-3.5 text-sm',
        variants[variant],
        className,
      )}
    >
      {loading && <Loader2 size={14} className="animate-spin" aria-hidden="true" />}
      {children}
    </button>
  )
}

export function IconButton({
  label,
  tone = 'default',
  className,
  children,
  ...props
}: ButtonHTMLAttributes<HTMLButtonElement> & { label: string; tone?: 'default' | 'danger' }) {
  return (
    <button
      {...props}
      aria-label={label}
      title={label}
      className={cx(
        'grid h-8 w-8 place-items-center rounded-lg text-muted transition-colors disabled:pointer-events-none disabled:opacity-30',
        tone === 'danger' ? 'hover:bg-danger/10 hover:text-danger' : 'hover:bg-subtle hover:text-fg',
        className,
      )}
    >
      {children}
    </button>
  )
}

export function Card({ className, children }: { className?: string; children: ReactNode }) {
  return <section className={cx('rounded-xl bg-card ring-1 ring-line', className)}>{children}</section>
}

export function CardHeader({ title, actions }: { title: ReactNode; actions?: ReactNode }) {
  return (
    <div className="flex items-center gap-3 border-b border-line px-4 py-3">
      <h2 className="text-sm font-semibold">{title}</h2>
      {actions && <div className="ml-auto flex items-center gap-2">{actions}</div>}
    </div>
  )
}

export function Avatar({ name, size = 'md' }: { name: string; size?: 'sm' | 'md' }) {
  return (
    <span
      className={cx(
        'grid shrink-0 place-items-center rounded-full bg-accent/10 font-semibold text-accent-text ring-1 ring-inset ring-accent/20',
        size === 'sm' ? 'h-6 w-6 text-[10px]' : 'h-8 w-8 text-[11px]',
      )}
      aria-hidden="true"
    >
      {initials(name)}
    </span>
  )
}

export function Field({
  label,
  htmlFor,
  error,
  hint,
  children,
}: {
  label: string
  htmlFor?: string
  error?: string
  hint?: string
  children: ReactNode
}) {
  return (
    <div className="flex flex-col gap-1.5">
      <label htmlFor={htmlFor} className="text-sm font-medium text-fg">
        {label}
      </label>
      {children}
      {error ? (
        <p className="text-xs text-danger" role="alert">
          {error}
        </p>
      ) : hint ? (
        <p className="text-xs text-muted">{hint}</p>
      ) : null}
    </div>
  )
}

// eslint-disable-next-line react-refresh/only-export-components
export const inputClass = (error?: string | boolean, extra?: string) =>
  cx(
    'h-10 w-full rounded-lg bg-card px-3 text-sm text-fg ring-1 ring-inset placeholder:text-muted/70 focus:outline-none focus:ring-2 disabled:cursor-not-allowed disabled:bg-subtle disabled:text-muted',
    error ? 'ring-danger focus:ring-danger' : 'ring-line focus:ring-accent',
    extra,
  )

type Tone = 'info' | 'success' | 'warning' | 'danger'

export function Alert({ tone = 'info', children, className }: { tone?: Tone; children: ReactNode; className?: string }) {
  const tones: Record<Tone, string> = {
    info: 'bg-info/10 text-info ring-info/20',
    success: 'bg-success/10 text-success ring-success/20',
    warning: 'bg-warning/10 text-warning ring-warning/25',
    danger: 'bg-danger/10 text-danger ring-danger/20',
  }
  return (
    <div role={tone === 'danger' ? 'alert' : 'status'} className={cx('rounded-lg px-3 py-2 text-sm ring-1 ring-inset', tones[tone], className)}>
      {children}
    </div>
  )
}

export function Spinner({ label }: { label?: string }) {
  return (
    <div className="flex items-center justify-center gap-2 py-12 text-sm text-muted">
      <Loader2 size={18} className="animate-spin" aria-hidden="true" />
      {label}
    </div>
  )
}

/** Fundalul semitransparent + închidere cu Escape, comun pentru dialog și drawer. */
function useEscape(onClose: () => void) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && onClose()
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])
}

export function Dialog({
  title,
  icon,
  onClose,
  children,
  footer,
}: {
  title: string
  icon?: ReactNode
  onClose: () => void
  children: ReactNode
  footer: ReactNode
}) {
  useEscape(onClose)
  return (
    <div className="fixed inset-0 z-50 grid place-items-center p-4" role="dialog" aria-modal="true" aria-label={title}>
      <div className="absolute inset-0 animate-fade-in bg-overlay/50" onClick={onClose} />
      <div className="relative w-full max-w-md animate-fade-in rounded-2xl bg-card p-5 shadow-2xl ring-1 ring-line">
        <div className="flex gap-3">
          {icon}
          <div className="min-w-0 flex-1">
            <h2 className="text-base font-semibold">{title}</h2>
            <div className="mt-2">{children}</div>
          </div>
        </div>
        <div className="mt-5 flex justify-end gap-2">{footer}</div>
      </div>
    </div>
  )
}

export function Drawer({
  title,
  subtitle,
  onClose,
  children,
  footer,
  onSubmit,
  closeLabel = 'Close',
}: {
  title: string
  subtitle?: string
  closeLabel?: string
  onClose: () => void
  children: ReactNode
  footer: ReactNode
  onSubmit: () => void
}) {
  useEscape(onClose)
  return (
    <div className="fixed inset-0 z-50" role="dialog" aria-modal="true" aria-label={title}>
      <div className="absolute inset-0 animate-fade-in bg-overlay/50" onClick={onClose} />
      <form
        noValidate
        onSubmit={(e) => {
          e.preventDefault()
          onSubmit()
        }}
        className="absolute inset-y-0 right-0 flex w-full max-w-xl animate-slide-in flex-col bg-card shadow-2xl ring-1 ring-line"
      >
        <div className="flex items-start justify-between gap-4 border-b border-line px-5 py-4">
          <div>
            <h2 className="text-base font-semibold">{title}</h2>
            {subtitle && <p className="mt-0.5 text-sm text-muted">{subtitle}</p>}
          </div>
          <IconButton type="button" label={closeLabel} onClick={onClose}>
            <X size={18} />
          </IconButton>
        </div>
        <div className="flex flex-1 flex-col gap-5 overflow-y-auto px-5 py-5">{children}</div>
        <div className="flex items-center justify-end gap-2 border-t border-line px-5 py-3">{footer}</div>
      </form>
    </div>
  )
}

export function EmptyState({ icon, title, text }: { icon: ReactNode; title: string; text?: string }) {
  return (
    <div className="flex flex-col items-center gap-2 px-4 py-12 text-center">
      <span className="text-muted">{icon}</span>
      <p className="text-sm font-medium">{title}</p>
      {text && <p className="text-xs text-muted">{text}</p>}
    </div>
  )
}
