import i18n from '../i18n'

/** Afișare dată+oră în fusul orar local, în limba curentă. */
export function formatDateTime(utc: string | null | undefined): string {
  if (!utc) return '–'
  return new Date(utc).toLocaleString(i18n.resolvedLanguage === 'en' ? 'en-GB' : 'ro-RO', {
    dateStyle: 'short',
    timeStyle: 'short',
  })
}

/** ISO UTC → valoare pentru <input type="datetime-local"> (ora locală, fără secunde). */
export function toLocalInput(utc: string | Date): string {
  const d = typeof utc === 'string' ? new Date(utc) : utc
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
}

/** Valoare datetime-local (ora locală) → ISO UTC. */
export function fromLocalInput(value: string): string {
  return new Date(value).toISOString()
}

export function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`
}
