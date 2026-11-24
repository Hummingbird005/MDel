import { expect, test } from '@playwright/test'
import path from 'node:path'
import { ADMIN, APPROVER, browserLocalInput, createViaApi, inDays, localInput, pickUser } from './helpers'

test.describe('Flux complet de delegare', () => {
  test('creare cu aprobare și fișier → aprobare de alt utilizator → programată', async ({ browser }) => {
    const admin = await browser.newContext({ extraHTTPHeaders: ADMIN })
    const page = await admin.newPage()
    await page.goto('/')
    await page.getByRole('button', { name: 'Delegare nouă' }).click()

    await pickUser(page, 'from', 'ion', 'Ion Popescu')
    await pickUser(page, 'to', 'maria', 'Maria Ionescu')
    await page.locator('#start').fill(localInput(inDays(1)))
    await page.locator('#end').fill(localInput(inDays(4, 18)))
    await page.locator('#notes').fill('Concediu de odihnă')
    await page.locator('#files').setInputFiles({ name: 'aprobare.pdf', mimeType: 'application/pdf', buffer: Buffer.from('%PDF-1.4 test') })
    await expect(page.getByText('Înregistrat de')).toBeVisible()
    await page.getByRole('button', { name: 'Trimite spre aprobare' }).click()

    await expect(page).toHaveURL(/\/delegations\/\d+$/)
    const id = page.url().split('/').pop()!
    await expect(page.getByText('aprobare.pdf')).toBeVisible()
    await expect(page.locator('h1 + span', { hasText: 'În așteptare' })).toBeVisible()

    const approverContext = await browser.newContext({ extraHTTPHeaders: APPROVER })
    const approver = await approverContext.newPage()
    await approver.goto('/approvals')
    await approver.getByRole('button', { name: 'Aprobă' }).first().click()
    await approver.getByRole('dialog').getByRole('button', { name: 'Aprobă' }).click()
    await expect(approver.getByText(`Delegarea #${id} a fost aprobată`)).toBeVisible()

    await page.reload()
    await expect(page.getByText('Programată').first()).toBeVisible()
    await expect(page.getByText('Dev Approver').first()).toBeVisible() // „Aprobat de”
    await admin.close()
    await approverContext.close()
  })

  test('fără aprobare, începută acum → activă → oprită de administrator', async ({ browser }) => {
    const context = await browser.newContext({ extraHTTPHeaders: ADMIN })
    const page = await context.newPage()
    await page.goto('/delegations')
    await page.getByRole('button', { name: 'Delegare nouă' }).click()
    await pickUser(page, 'from', 'maria', 'Maria Ionescu')
    await pickUser(page, 'to', 'dev', 'Dev Approver')
    await page.locator('#start').fill(await browserLocalInput(page))
    await page.locator('#end').fill(localInput(inDays(2, 18)))
    await page.getByText('Fără aprobare', { exact: true }).click()
    await page.getByRole('button', { name: 'Creează delegarea' }).click()

    await expect(page).toHaveURL(/\/delegations\/\d+$/)
    await expect(page.getByText('Activă').first()).toBeVisible()

    // Delegarea pornită „acum” are ora de început cu secunde; formularul trimite minutul – modificarea sfârșitului merge.
    await page.getByRole('button', { name: 'Modifică data de sfârșit' }).click()
    await page.locator('#end').fill(localInput(inDays(3, 18)))
    await page.getByRole('button', { name: 'Salvează' }).click()
    await expect(page.getByRole('alert').filter({ hasText: 'doar data de sfârșit' })).toHaveCount(0)
    await expect(page.locator('#end')).toHaveCount(0) // formularul s-a închis după salvare
    await expect(page.getByText('Activă').first()).toBeVisible()

    await page.getByRole('button', { name: 'Oprește', exact: true }).click()
    await page.locator('#decision-reason').fill('Revenire anticipată')
    await page.getByRole('dialog').getByRole('button', { name: 'Oprește', exact: true }).click()
    await expect(page.getByText('Oprită').first()).toBeVisible()
    await expect(page.getByText('Revenire anticipată').first()).toBeVisible()
    await context.close()
  })

  test('suprapunerea pe aceeași căsuță sursă este refuzată cu mesaj pe câmp', async ({ browser, request }) => {
    await createViaApi(request, { fromSam: 'dev.admin', toSam: 'ion.popescu', startUtc: inDays(5), endUtc: inDays(8), requiresApproval: true })

    const context = await browser.newContext({ extraHTTPHeaders: ADMIN })
    const page = await context.newPage()
    await page.goto('/')
    await page.getByRole('button', { name: 'Delegare nouă' }).click()
    await pickUser(page, 'from', 'dev.ad', 'Dev Admin')
    await pickUser(page, 'to', 'maria', 'Maria Ionescu')
    await page.locator('#start').fill(localInput(inDays(6)))
    await page.locator('#end').fill(localInput(inDays(7)))
    await page.getByRole('button', { name: 'Trimite spre aprobare' }).click()
    await expect(page.getByText(/are deja o delegare/)).toBeVisible()
    await context.close()
  })

  test('activare și expirare automată la data și ora setate', async ({ browser, request }) => {
    const now = Date.now()
    const created = await createViaApi(request, {
      fromSam: 'ion.popescu',
      toSam: 'dev.approver',
      startUtc: new Date(now + 6_000),
      endUtc: new Date(now + 20_000),
      requiresApproval: false,
    })
    expect(created.status).toBe('Scheduled')

    const context = await browser.newContext({ extraHTTPHeaders: ADMIN })
    const page = await context.newPage()
    await page.goto(`/delegations/${created.id}`)
    await expect(async () => {
      await page.reload()
      await expect(page.getByText('Activă').first()).toBeVisible({ timeout: 1_000 })
    }).toPass({ timeout: 30_000 })
    await expect(async () => {
      await page.reload()
      await expect(page.getByText('Expirată').first()).toBeVisible({ timeout: 1_000 })
    }).toPass({ timeout: 40_000 })
    await expect(page.getByText('Sistem').first()).toBeVisible() // tranzițiile automate
    await context.close()
  })

  test('fișierul atașat se descarcă cu numele original', async ({ browser, request }) => {
    const created = await createViaApi(request, { fromSam: 'maria.ionescu', toSam: 'ion.popescu', startUtc: inDays(10), endUtc: inDays(12), requiresApproval: true })
    const upload = await request.post(`http://localhost:5080/api/delegations/${created.id}/attachments`, {
      headers: ADMIN,
      multipart: { files: { name: 'cerere.txt', mimeType: 'text/plain', buffer: Buffer.from('cerere scrisă') } },
    })
    expect(upload.status()).toBe(200)

    const context = await browser.newContext({ extraHTTPHeaders: ADMIN, acceptDownloads: true })
    const page = await context.newPage()
    await page.goto(`/delegations/${created.id}`)
    const [download] = await Promise.all([page.waitForEvent('download'), page.getByRole('link', { name: 'cerere.txt' }).click()])
    expect(download.suggestedFilename()).toBe('cerere.txt')
    expect(path.extname(await download.path())).toBe('')
    await context.close()
  })
})
