import { expect, test } from '@playwright/test'
import { ADMIN, APPROVER, NO_ROLE } from './helpers'

test('tema dark se păstrează după reîncărcare', async ({ browser }) => {
  const context = await browser.newContext({ extraHTTPHeaders: ADMIN, colorScheme: 'light' })
  const page = await context.newPage()
  await page.goto('/')
  await page.getByRole('button', { name: 'Temă întunecată' }).click()
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark')
  await page.reload()
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark')
  await expect(page.getByRole('button', { name: 'Temă luminoasă' })).toBeVisible()
  await context.close()
})

test('limba se schimbă în engleză', async ({ browser }) => {
  const context = await browser.newContext({ extraHTTPHeaders: { ...ADMIN, 'X-Dev-User': 'CONTOSO\\maria.ionescu' } })
  const page = await context.newPage()
  await page.goto('/')
  await page.locator('#language').selectOption('en')
  await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'New delegation' })).toBeVisible()
  await context.close()
})

test('rolurile limitează accesul la pagini', async ({ browser }) => {
  const approver = await (await browser.newContext({ extraHTTPHeaders: APPROVER })).newPage()
  await approver.goto('/admin')
  await expect(approver.getByText('Nu aveți drepturi pentru această pagină.')).toBeVisible()
  await expect(approver.getByRole('link', { name: 'Aprobări' })).toBeVisible()
  await expect(approver.getByRole('link', { name: 'Setări' })).toHaveCount(0)

  const user = await (await browser.newContext({ extraHTTPHeaders: NO_ROLE })).newPage()
  await user.goto('/')
  await expect(user.getByText('Nu aveți drepturi pentru această pagină.')).toBeVisible()
})

test('auditul arată acțiunile și detaliul cu valori vechi/noi', async ({ browser, request }) => {
  const r = await request.put('http://localhost:5080/api/admin/settings', {
    headers: ADMIN,
    data: { values: { 'Retention.AuditDays': '400' } },
  })
  expect(r.status()).toBe(204)

  const page = await (await browser.newContext({ extraHTTPHeaders: ADMIN })).newPage()
  await page.goto('/audit')
  await page.locator('#audit-action').selectOption('Settings.')
  await page.locator('tbody tr').first().click()
  const dialog = page.getByRole('dialog')
  await expect(dialog.getByRole('cell', { name: 'Retention.AuditDays', exact: true })).toBeVisible()
  await expect(dialog.getByRole('cell', { name: '400', exact: true })).toBeVisible()
})

test('pe telefon pagina nu are scroll orizontal', async ({ browser }) => {
  const context = await browser.newContext({ extraHTTPHeaders: ADMIN, viewport: { width: 390, height: 844 } })
  const page = await context.newPage()
  for (const url of ['/', '/delegations', '/audit', '/admin']) {
    await page.goto(url)
    await page.waitForLoadState('networkidle')
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)
    expect(overflow, url).toBe(0)
  }
  await context.close()
})
