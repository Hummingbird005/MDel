import { Check, Info, X } from 'lucide-react'
import { createContext, useCallback, useContext, useState, type ReactNode } from 'react'
import { cx } from '../../lib/cx'

type Tone = 'success' | 'info' | 'danger'
interface Toast {
  id: number
  text: string
  tone: Tone
}

const ToastContext = createContext<(text: string, tone?: Tone) => void>(() => {})

// eslint-disable-next-line react-refresh/only-export-components
export const useToast = () => useContext(ToastContext)

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([])

  const push = useCallback((text: string, tone: Tone = 'success') => {
    const id = Date.now() + Math.random()
    setToasts((t) => [...t, { id, text, tone }])
    setTimeout(() => setToasts((t) => t.filter((x) => x.id !== id)), 4000)
  }, [])

  return (
    <ToastContext.Provider value={push}>
      {children}
      <div className="pointer-events-none fixed bottom-4 right-4 z-[70] flex flex-col gap-2" aria-live="polite">
        {toasts.map((t) => (
          <div
            key={t.id}
            className="pointer-events-auto flex max-w-sm animate-slide-in items-center gap-2.5 rounded-xl bg-card px-4 py-3 text-sm text-fg shadow-lg ring-1 ring-line"
          >
            <span
              className={cx(
                'grid h-5 w-5 shrink-0 place-items-center rounded-full',
                t.tone === 'danger' ? 'bg-danger/15 text-danger' : t.tone === 'info' ? 'bg-info/15 text-info' : 'bg-success/15 text-success',
              )}
            >
              {t.tone === 'danger' ? <X size={12} /> : t.tone === 'info' ? <Info size={12} /> : <Check size={12} />}
            </span>
            {t.text}
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  )
}
