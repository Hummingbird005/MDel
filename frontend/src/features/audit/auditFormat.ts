import type { TFunction } from 'i18next'
import type { AuditItem } from '../../api/audit'
import { formatDateTime } from '../../lib/format'

/** Valorile vechi / noi dintr-o înregistrare de audit (JSON) ca obiect plat. */
export function parseValues(json: string | null | undefined): Record<string, unknown> | null {
  if (!json) return null
  try {
    const v = JSON.parse(json)
    return v && typeof v === 'object' && !Array.isArray(v) ? (v as Record<string, unknown>) : { value: v }
  } catch {
    return { value: json }
  }
}

const isoDate = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d+)?)?(Z|[+-]\d{2}:\d{2})?$/

/** Eticheta câmpului: setările au etichetele din ecranul Setări, restul din audit.fields; codul rămâne ca rezervă. */
export function fieldLabel(t: TFunction, action: string, key: string): string {
  if (action.startsWith('Settings.')) return t(`admin.fields.${key.replace(/\./g, '_')}`, { defaultValue: key })
  return t(`audit.fields.${key}`, { defaultValue: key })
}

/** Valoare lizibilă: date în ora locală, da/nu, statusuri traduse; textul rămâne exact cum a fost introdus. */
export function formatValue(t: TFunction, key: string, value: unknown): string {
  // Lipsă, null și text gol sunt același lucru pentru utilizator: „–”.
  if (value === undefined || value === null || value === '') return '–'
  if (typeof value === 'boolean') return value ? t('admin.yes') : t('admin.no')
  if (Array.isArray(value)) {
    // Liste (ex. rolurile la autentificare): „Administrator delegări, Aprobator”, nu JSON.
    const roleKey = key === 'roles' || key === 'initialAdminRoles'
    return value.map((v) => (roleKey ? t(`roles.${String(v)}`, { defaultValue: String(v) }) : String(v))).join(', ') || '–'
  }
  if (typeof value === 'string') {
    if (isoDate.test(value)) return formatDateTime(/(Z|[+-]\d{2}:\d{2})$/.test(value) ? value : `${value}Z`)
    if (key === 'Status') return t(`status.${value}`, { defaultValue: value })
    return value
  }
  return typeof value === 'object' ? JSON.stringify(value) : String(value)
}

export interface FieldChange {
  key: string
  label: string
  before: string
  after: string
  changed: boolean
}

/** Câmpurile unei înregistrări, cu valorile formatate; `changed` doar când există și valori vechi, și noi. */
export function fieldChanges(t: TFunction, action: string, oldJson: string | null, newJson: string | null): FieldChange[] {
  const oldV = parseValues(oldJson)
  const newV = parseValues(newJson)
  const keys = Array.from(new Set([...Object.keys(oldV ?? {}), ...Object.keys(newV ?? {})]))
  return keys.map((key) => {
    const before = formatValue(t, key, oldV?.[key])
    const after = formatValue(t, key, newV?.[key])
    return { key, label: fieldLabel(t, action, key), before, after, changed: !!oldV && !!newV && before !== after }
  })
}

const MAX_FIELDS = 8
/** La creare / încercare, în rezumat intră ce a introdus utilizatorul; ce derivă din setări apare doar în detaliu. */
const DERIVED_FIELDS = new Set(['KeepCopyInSource', 'Status'])
const clip = (text: string, max = 80) => (text.length > max ? `${text.slice(0, max - 1)}…` : text)

/**
 * Ce s-a întâmplat, într-o frază scurtă pentru lista de audit:
 * la modificare „Câmp: vechi → nou”, la creare „Câmp: valoare” pentru ce s-a introdus.
 */
export function summarize(t: TFunction, item: Pick<AuditItem, 'action' | 'oldValues' | 'newValues'>): string {
  const all = fieldChanges(t, item.action, item.oldValues, item.newValues)
  if (all.length === 0) return ''

  const hasOld = !!item.oldValues
  const hasNew = !!item.newValues
  const shown =
    hasOld && hasNew ? all.filter((f) => f.changed) : all.filter((f) => (f.after !== '–' || !hasNew) && !(hasNew && DERIVED_FIELDS.has(f.key)))
  const parts = shown.slice(0, MAX_FIELDS).map((f) =>
    hasOld && hasNew ? `${f.label}: ${clip(f.before, 40)} → ${clip(f.after, 40)}` : `${f.label}: ${clip(hasNew ? f.after : f.before)}`,
  )
  const more = shown.length - MAX_FIELDS
  return more > 0 ? `${parts.join(' · ')} · ${t('audit.summaryMore', { count: more })}` : parts.join(' · ')
}
