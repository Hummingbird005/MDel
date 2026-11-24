import { request } from '@playwright/test'
import fs from 'node:fs'
import os from 'node:os'
import path from 'node:path'

/** Configurează aplicația de test: calea fișierelor și adresele grupurilor (altfel uploadul și emailurile eșuează). */
export default async function globalSetup() {
  const storage = fs.mkdtempSync(path.join(os.tmpdir(), 'mdel-e2e-'))
  const api = await request.newContext({
    baseURL: 'http://localhost:5080',
    extraHTTPHeaders: { 'X-Dev-User': 'CONTOSO\\dev.admin', 'X-Dev-Groups': 'CONTOSO\\MDel-System' },
  })
  const response = await api.put('/api/admin/settings', {
    data: {
      values: {
        'Attachments.StoragePath': storage,
        'Groups.ApproversEmail': 'mdel-approvers@contoso.local',
        'Groups.AdminsEmail': 'mdel-admins@contoso.local',
      },
    },
  })
  if (response.status() !== 204) throw new Error(`E2E setup failed: ${response.status()} ${await response.text()}`)
  await api.dispose()
}
