import { expect, type APIRequestContext, type Page } from '@playwright/test'

/** Administrator delegări + administrator sistem: cele mai multe teste deschid și Setări / Audit. */
export const ADMIN = { 'X-Dev-User': 'CONTOSO\\dev.admin', 'X-Dev-Groups': 'CONTOSO\\MDel-Admins,CONTOSO\\MDel-System' }
/** Doar delegări: fără Setări și Audit. */
export const DELEGATION_ADMIN = { 'X-Dev-User': 'CONTOSO\\dev.delegations', 'X-Dev-Groups': 'CONTOSO\\MDel-Admins' }
/** Doar Setări și Audit: fără delegări. */
export const SYSTEM_ADMIN = { 'X-Dev-User': 'CONTOSO\\dev.system', 'X-Dev-Groups': 'CONTOSO\\MDel-System' }
export const APPROVER = { 'X-Dev-User': 'CONTOSO\\dev.approver', 'X-Dev-Groups': 'CONTOSO\\MDel-Approvers' }
export const NO_ROLE = { 'X-Dev-User': 'CONTOSO\\ion.popescu', 'X-Dev-Groups': 'none' }

const pad = (n: number) => String(n).padStart(2, '0')

/** „Acum + offset” ca valoare datetime-local, calculată în fusul orar al browserului (nu al mașinii de test). */
export const browserLocalInput = (page: Page, offsetMs = 0) =>
  page.evaluate((offset) => {
    const d = new Date(Date.now() + offset)
    const p = (n: number) => String(n).padStart(2, '0')
    return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}T${p(d.getHours())}:${p(d.getMinutes())}`
  }, offsetMs)

/** Valoare pentru <input type="datetime-local"> (ora locală a mașinii; bună pentru date la zile distanță). */
export function localInput(date: Date) {
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`
}

export const inDays = (days: number, hour = 9) => {
  const d = new Date(Date.now() + days * 86_400_000)
  d.setHours(hour, 0, 0, 0)
  return d
}

/** Selectează un utilizator în combobox-ul AD. */
export async function pickUser(page: Page, id: string, search: string, name: string) {
  await page.locator(`#${id}`).click()
  await page.locator(`#${id}`).fill(search)
  await page.getByRole('option', { name: new RegExp(name) }).first().click()
}

/** Creează o delegare direct prin API (pentru teste în care formularul nu e subiectul). */
export async function createViaApi(
  request: APIRequestContext,
  body: { fromSam: string; toSam: string; startUtc: Date; endUtc: Date; requiresApproval: boolean },
) {
  const response = await request.post('http://localhost:5080/api/delegations', {
    headers: ADMIN,
    data: { ...body, startUtc: body.startUtc.toISOString(), endUtc: body.endUtc.toISOString() },
  })
  expect(response.status(), await response.text()).toBe(201)
  return (await response.json()) as { id: number; status: string }
}
