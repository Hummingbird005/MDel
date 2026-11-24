import { api } from './client'

export interface SettingView {
  key: string
  category: string
  value: string | null
  isSecret: boolean
  hasValue: boolean
  updatedUtc: string
  updatedBy: string | null
}

export interface SettingsResponse {
  settings: SettingView[]
  maxAttachmentMb: number
}

export interface ConnectionTestResult {
  success: boolean
  message: string
}

export interface MailboxForwarding {
  forwardingAddress: string | null
  deliverToMailboxAndForward: boolean
  forwardingSmtpAddress: string | null
}

export interface InformGroupMember {
  id: number
  user: { id: number; samAccountName: string; displayName: string; email: string | null }
  addedBy: string
  addedUtc: string
}

export type OutboxFilter = 'Failed' | 'Pending' | 'Sent' | 'All'

export interface OutboxItem {
  id: number
  delegationId: number
  recipientEmail: string
  recipientName: string | null
  recipientType: string
  event: string
  subject: string
  createdUtc: string
  sentUtc: string | null
  attempts: number
  lastAttemptUtc: string | null
  error: string | null
}

export type TemplateKind = 'Request' | 'Pending' | 'Direct' | 'Approved' | 'Rejected' | 'Activated' | 'Expired' | 'Stopped' | 'Failed'

export interface TemplatesMeta {
  templates: { kind: TemplateKind; subjectKey: string; bodyKey: string; defaultSubject: string; defaultBody: string }[]
  placeholders: string[]
}

export type TestTarget = 'ad' | 'exchange' | 'smtp' | 'storage'

export const adminApi = {
  getSettings: () => api.get<SettingsResponse>('/api/admin/settings'),
  // Secrete: null = neschimbat, "" = șters.
  updateSettings: (values: Record<string, string | null>) => api.put<void>('/api/admin/settings', { values }),
  test: (target: TestTarget, body?: unknown) => api.post<ConnectionTestResult>(`/api/admin/test/${target}`, body ?? {}),
  informGroup: () => api.get<InformGroupMember[]>('/api/admin/inform-group'),
  addInformMember: (samAccountName: string) => api.post<InformGroupMember[]>('/api/admin/inform-group', { samAccountName }),
  removeInformMember: (id: number) => api.delete<void>(`/api/admin/inform-group/${id}`),
  notifications: (filter: OutboxFilter) => api.get<OutboxItem[]>(`/api/admin/notifications?filter=${filter}`),
  failedNotifications: () => api.get<number>('/api/admin/notifications/failed-count'),
  retryNotification: (id: number) => api.post<void>(`/api/admin/notifications/${id}/retry`),
  templates: () => api.get<TemplatesMeta>('/api/admin/templates'),
  previewTemplate: (kind: TemplateKind, subject: string, body: string) =>
    api.post<{ subject: string; html: string }>('/api/admin/templates/preview', { kind, subject, body }),
  getForwarding: (mailbox: string) =>
    api.get<MailboxForwarding>(`/api/admin/exchange/forwarding?mailbox=${encodeURIComponent(mailbox)}`),
}
