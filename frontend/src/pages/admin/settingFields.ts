import type { TestTarget } from '../../api/admin'

export type FieldKind = 'text' | 'secret' | 'bool' | 'number' | 'language'

/** Ordinea tab-urilor și testul de conexiune asociat fiecăreia. */
export const categories: { id: string; test?: TestTarget }[] = [
  { id: 'ActiveDirectory', test: 'ad' },
  { id: 'Groups' },
  { id: 'Exchange', test: 'exchange' },
  { id: 'Smtp', test: 'smtp' },
  { id: 'Attachments', test: 'storage' },
  { id: 'Retention' },
  { id: 'General' },
]

const kinds: Record<string, FieldKind> = {
  'Exchange.KeepCopyInSource': 'bool',
  'Smtp.UseTls': 'bool',
  'General.PreventSelfApproval': 'bool',
  'Smtp.Port': 'number',
  'Attachments.MaxSizeMb': 'number',
  'Retention.AttachmentsDays': 'number',
  'Retention.AuditDays': 'number',
  'General.DefaultLanguage': 'language',
}

export function fieldKind(key: string, isSecret: boolean): FieldKind {
  return isSecret ? 'secret' : (kinds[key] ?? 'text')
}

/** Cheile i18n nu pot conține puncte – "Smtp.Host" devine "Smtp_Host". */
export const labelKey = (key: string) => `admin.fields.${key.replace(/\./g, '_')}`
export const helpKey = (key: string) => `admin.help.${key.replace(/\./g, '_')}`
