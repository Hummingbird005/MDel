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
  createdBy: UserRef
  createdUtc: string
  attachmentCount: number
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
  actions: { canEdit: boolean; canEditEndOnly: boolean; canStop: boolean; canManageAttachments: boolean }
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

export const delegationsApi = {
  list: (params: { status?: DelegationStatus[]; search?: string; page?: number; pageSize?: number }) => {
    const q = new URLSearchParams()
    params.status?.forEach((s) => q.append('status', s))
    if (params.search) q.set('search', params.search)
    q.set('page', String(params.page ?? 1))
    q.set('pageSize', String(params.pageSize ?? 25))
    return api.get<PagedResult<DelegationListItem>>(`/api/delegations?${q}`)
  },
  summary: () => api.get<Record<DelegationStatus, number>>('/api/delegations/summary'),
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
