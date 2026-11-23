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

export type TestTarget = 'ad' | 'exchange' | 'smtp' | 'storage'

export const adminApi = {
  getSettings: () => api.get<SettingsResponse>('/api/admin/settings'),
  // Secrete: null = neschimbat, "" = șters.
  updateSettings: (values: Record<string, string | null>) => api.put<void>('/api/admin/settings', { values }),
  test: (target: TestTarget, body?: unknown) => api.post<ConnectionTestResult>(`/api/admin/test/${target}`, body ?? {}),
  informGroup: () => api.get<InformGroupMember[]>('/api/admin/inform-group'),
  addInformMember: (samAccountName: string) => api.post<InformGroupMember[]>('/api/admin/inform-group', { samAccountName }),
  removeInformMember: (id: number) => api.delete<void>(`/api/admin/inform-group/${id}`),
  getForwarding: (mailbox: string) =>
    api.get<MailboxForwarding>(`/api/admin/exchange/forwarding?mailbox=${encodeURIComponent(mailbox)}`),
}
