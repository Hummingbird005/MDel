import i18n from '../i18n'

const locale = () => (i18n.resolvedLanguage === 'en' ? 'en-GB' : 'ro-RO')

/** Dată + oră în fusul orar local, în limba curentă. */
export function formatDateTime(utc: string | null | undefined): string {
  if (!utc) return '–'
  return new Date(utc).toLocaleString(locale(), { day: 'numeric', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' })
}

export const formatDate = (utc: string) => new Date(utc).toLocaleDateString(locale(), { day: 'numeric', month: 'short' })
export const formatTime = (utc: string) => new Date(utc).toLocaleTimeString(locale(), { hour: '2-digit', minute: '2-digit' })

/** ISO UTC → valoare pentru <input type="datetime-local"> (ora locală). */
export function toLocalInput(utc: string | Date): string {
  const d = typeof utc === 'string' ? new Date(utc) : utc
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
}

/** Valoare datetime-local (ora locală) → ISO UTC. */
export const fromLocalInput = (value: string) => (value ? new Date(value).toISOString() : '')

export function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`
}

export const DAY_MS = 86_400_000
