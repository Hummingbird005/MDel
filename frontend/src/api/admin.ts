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

export type TestTarget = 'ad' | 'exchange' | 'smtp' | 'storage'

export const adminApi = {
  getSettings: () => api.get<SettingsResponse>('/api/admin/settings'),
  // Secrete: null = neschimbat, "" = șters.
  updateSettings: (values: Record<string, string | null>) => api.put<void>('/api/admin/settings', { values }),
  test: (target: TestTarget, body?: unknown) => api.post<ConnectionTestResult>(`/api/admin/test/${target}`, body ?? {}),
  getForwarding: (mailbox: string) =>
    api.get<MailboxForwarding>(`/api/admin/exchange/forwarding?mailbox=${encodeURIComponent(mailbox)}`),
}
