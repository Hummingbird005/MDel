import { api } from './client'

export type AuditResult = 'Success' | 'Failure' | 'Denied'

export interface AuditItem {
  id: number
  timestampUtc: string
  userName: string
  userDisplayName: string | null
  action: string
  entityType: string | null
  entityId: string | null
  details: string | null
  httpMethod: string | null
  path: string | null
  statusCode: number | null
  ipAddress: string | null
  result: AuditResult
  hasChanges: boolean
}

export interface AuditDetail extends Omit<AuditItem, 'hasChanges'> {
  correlationId: string | null
  oldValues: string | null
  newValues: string | null
}

export interface AuditFilters {
  from?: string
  to?: string
  user?: string
  action?: string
  entityType?: string
  entityId?: string
  result?: AuditResult | ''
  search?: string
}

const query = (f: AuditFilters, page?: number, pageSize?: number) => {
  const q = new URLSearchParams()
  Object.entries(f).forEach(([k, v]) => v && q.set(k, v))
  if (page) q.set('page', String(page))
  if (pageSize) q.set('pageSize', String(pageSize))
  return q
}

export const auditApi = {
  list: (f: AuditFilters, page: number, pageSize: number) =>
    api.get<{ items: AuditItem[]; total: number; page: number; pageSize: number }>(`/api/audit?${query(f, page, pageSize)}`),
  get: (id: number) => api.get<AuditDetail>(`/api/audit/${id}`),
  actions: () => api.get<string[]>('/api/audit/actions'),
  exportUrl: (f: AuditFilters) => `/api/audit/export?${query(f)}`,
}
