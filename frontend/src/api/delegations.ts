import { api, ApiError } from './client'

export type DelegationStatus = 'PendingApproval' | 'Scheduled' | 'Active' | 'Expired' | 'Rejected' | 'Stopped' | 'Failed'

export const allStatuses: DelegationStatus[] = [
  'Active',
  'Scheduled',
  'PendingApproval',
  'Expired',
  'Stopped',
  'Rejected',
  'Failed',
]

export interface UserRef {
  id: number
  samAccountName: string
  displayName: string
  email: string | null
}

export interface DirectoryUser {
  objectId: string
  samAccountName: string
  displayName: string
  email: string | null
  hasMailbox: boolean
}

export interface DelegationListItem {
  id: number
  status: DelegationStatus
  from: UserRef
  to: UserRef
  startUtc: string
  endUtc: string
  requiresApproval: boolean
  keepCopyInSource: boolean
  createdBy: UserRef
  createdUtc: string
  attachmentCount: number
}

export interface DelegationStats {
  byStatus: Record<DelegationStatus, number>
  expiringIn7Days: number
  expiredLast30Days: number
}

export interface PagedResult<T> {
  items: T[]
  total: number
  page: number
  pageSize: number
}

export interface Attachment {
  id: number
  fileName: string
  sizeBytes: number
  contentType: string
  uploadedBy: UserRef
  uploadedUtc: string
  purged: boolean
}

export interface Notification {
  recipientEmail: string
  recipientName: string | null
  recipientType: 'ApproverGroup' | 'AdminGroup' | 'SourceUser' | 'TargetUser' | 'InformGroup'
  event: string
  createdUtc: string
  sentUtc: string | null
  error: string | null
}

export interface StatusHistory {
  fromStatus: DelegationStatus | null
  toStatus: DelegationStatus
  changedBy: string | null
  changedUtc: string
  reason: string | null
}

export interface DelegationDetail {
  id: number
  status: DelegationStatus
  from: UserRef
  to: UserRef
  startUtc: string
  endUtc: string
  requiresApproval: boolean
  keepCopyInSource: boolean
  notes: string | null
  createdBy: UserRef
  createdUtc: string
  approvedBy: UserRef | null
  approvedUtc: string | null
  rejectReason: string | null
  stoppedBy: UserRef | null
  stoppedUtc: string | null
  stopReason: string | null
  previousForwardingAddress: string | null
  exchangeAppliedUtc: string | null
  exchangeRemovedUtc: string | null
  lastError: string | null
  rowVersion: string
  attachments: Attachment[]
  notifications: Notification[]
  history: StatusHistory[]
  actions: {
    canEdit: boolean
    canEditEndOnly: boolean
    canStop: boolean
    canManageAttachments: boolean
    canApprove: boolean
    canReject: boolean
    isOwnRequest: boolean
  }
}

export interface SaveDelegationRequest {
  fromSam: string
  toSam: string
  startUtc: string
  endUtc: string
  requiresApproval: boolean
  notes: string | null
  rowVersion?: string
}

export interface FormConfig {
  maxAttachmentMb: number
  allowedExtensions: string[]
  keepCopyInSource: boolean
}

export interface SourceCheck {
  currentForwarding: { forwardingAddress: string | null; forwardingSmtpAddress: string | null } | null
  exchangeError: string | null
  openDelegations: DelegationListItem[]
}

export type SortKey = 'Default' | 'Start' | 'End' | 'Created' | 'Id'

export interface ListParams {
  status?: DelegationStatus[]
  search?: string
  keepCopy?: boolean
  sort?: SortKey
  desc?: boolean
  fromUtc?: string
  toUtc?: string
  page?: number
  pageSize?: number
}

export interface UpcomingEvent {
  type: 'Activation' | 'Expiration'
  atUtc: string
  delegation: DelegationListItem
}

function toQuery(p: ListParams): URLSearchParams {
  const q = new URLSearchParams()
  p.status?.forEach((s) => q.append('status', s))
  if (p.keepCopy !== undefined) q.set('keepCopy', String(p.keepCopy))
  if (p.search) q.set('search', p.search)
  if (p.sort && p.sort !== 'Default') q.set('sort', p.sort)
  if (p.desc) q.set('desc', 'true')
  if (p.fromUtc) q.set('from', p.fromUtc)
  if (p.toUtc) q.set('to', p.toUtc)
  if (p.page !== undefined) q.set('page', String(p.page))
  if (p.pageSize !== undefined) q.set('pageSize', String(p.pageSize))
  return q
}

export const delegationsApi = {
  list: (params: ListParams) => api.get<PagedResult<DelegationListItem>>(`/api/delegations?${toQuery(params)}`),
  /** URL de descărcare – browserul trimite autentificarea Windows, ca la orice link. */
  exportUrl: (params: ListParams) => `/api/delegations/export?${toQuery({ ...params, page: undefined, pageSize: undefined })}`,
  upcoming: (days = 7) => api.get<UpcomingEvent[]>(`/api/delegations/upcoming?days=${days}`),
  stats: () => api.get<DelegationStats>('/api/delegations/stats'),
  approve: (id: number, rowVersion: string) => api.post<DelegationDetail>(`/api/delegations/${id}/approve`, { rowVersion }),
  reject: (id: number, reason: string, rowVersion: string) =>
    api.post<DelegationDetail>(`/api/delegations/${id}/reject`, { reason, rowVersion }),
  get: (id: number) => api.get<DelegationDetail>(`/api/delegations/${id}`),
  formConfig: () => api.get<FormConfig>('/api/delegations/form-config'),
  sourceCheck: (fromSam: string) =>
    api.get<SourceCheck>(`/api/delegations/source-check?fromSam=${encodeURIComponent(fromSam)}`),
  create: (body: SaveDelegationRequest) => api.post<DelegationDetail>('/api/delegations', body),
  update: (id: number, body: SaveDelegationRequest) => api.put<DelegationDetail>(`/api/delegations/${id}`, body),
  stop: (id: number, reason: string, rowVersion: string) =>
    api.post<DelegationDetail>(`/api/delegations/${id}/stop`, { reason, rowVersion }),
  deleteAttachment: (id: number, attachmentId: number) =>
    api.delete<void>(`/api/delegations/${id}/attachments/${attachmentId}`),
  attachmentUrl: (id: number, attachmentId: number) => `/api/delegations/${id}/attachments/${attachmentId}`,
  searchUsers: (q: string) => api.get<DirectoryUser[]>(`/api/directory/users?q=${encodeURIComponent(q)}`),
}

/** Upload cu progres (XMLHttpRequest) – câte un fișier pe cerere, ca limita serverului să fie per fișier. */
export function uploadAttachment(id: number, file: File, onProgress: (percent: number) => void): Promise<void> {
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest()
    xhr.open('POST', `/api/delegations/${id}/attachments`)
    xhr.withCredentials = true
    xhr.setRequestHeader('Accept-Language', localStorage.getItem('mdel.lang') ?? 'ro')
    xhr.upload.onprogress = (e) => e.lengthComputable && onProgress(Math.round((e.loaded / e.total) * 100))
    xhr.onload = () => {
      if (xhr.status >= 200 && xhr.status < 300) return resolve()
      let message = `HTTP ${xhr.status}`
      let errors: Record<string, string[]> | undefined
      try {
        const problem = JSON.parse(xhr.responseText)
        errors = problem.errors
        message = errors ? Object.values(errors).flat().join(' ') : (problem.detail ?? problem.title ?? message)
      } catch {
        if (xhr.status === 413) message = 'File too large for the server (HTTP 413).'
      }
      reject(new ApiError(xhr.status, message, errors))
    }
    xhr.onerror = () => reject(new ApiError(0, 'Network error'))
    const form = new FormData()
    form.append('files', file, file.name)
    xhr.send(form)
  })
}
