import { expect, test } from '@playwright/test'
import { ADMIN, APPROVER, DELEGATION_ADMIN, NO_ROLE, SYSTEM_ADMIN } from './helpers'

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

test('administratorul de delegări nu vede Setări și Audit, administratorul de sistem nu vede delegările', async ({ browser }) => {
  const delegations = await (await browser.newContext({ extraHTTPHeaders: DELEGATION_ADMIN })).newPage()
  await delegations.goto('/delegations')
  await expect(delegations.getByRole('link', { name: 'Delegări' })).toBeVisible()
  await expect(delegations.getByRole('link', { name: 'Setări' })).toHaveCount(0)
  await expect(delegations.getByRole('link', { name: /Audit/ })).toHaveCount(0)
  await delegations.goto('/admin')
  await expect(delegations.getByText('Nu aveți drepturi pentru această pagină.')).toBeVisible()
  await delegations.goto('/audit')
  await expect(delegations.getByText('Nu aveți drepturi pentru această pagină.')).toBeVisible()

  const system = await (await browser.newContext({ extraHTTPHeaders: SYSTEM_ADMIN })).newPage()
  await system.goto('/admin')
  await expect(system.getByRole('tab', { name: 'Active Directory' })).toBeVisible()
  await expect(system.getByRole('link', { name: 'Setări' })).toBeVisible()
  await expect(system.getByRole('link', { name: /Audit/ })).toBeVisible()
  await expect(system.getByRole('link', { name: 'Delegări' })).toHaveCount(0)
  await system.goto('/delegations')
  await expect(system.getByText('Nu aveți drepturi pentru această pagină.')).toBeVisible()
})

test('Active Directory se configurează din Setări, iar contul și parola nu se mai afișează', async ({ browser, request }) => {
  const page = await (await browser.newContext({ extraHTTPHeaders: SYSTEM_ADMIN })).newPage()
  await page.goto('/admin')
  await page.getByLabel('Server LDAP / controler de domeniu').fill('dc01.corp.local')
  await page.getByRole('switch', { name: 'Folosește SSL (LDAPS)' }).click()
  await page.getByLabel('Port').fill('636')
  await page.getByLabel('OU-uri de căutare').fill('OU=Angajati,DC=corp,DC=local\nOU=Externi,DC=corp,DC=local')
  await page.getByLabel('Cont de citire din AD').fill('CN=svc-mdel,OU=Servicii,DC=corp,DC=local')
  await page.getByLabel('Parola contului').fill('Parola-Secreta-1')
  await page.getByRole('button', { name: 'Salvează' }).click()
  await expect(page.getByText('Setările au fost salvate.')).toBeVisible()

  // După reîncărcare: cele nesecrete se văd, contul și parola apar doar ca „setate”.
  await page.reload()
  await expect(page.getByLabel('Server LDAP / controler de domeniu')).toHaveValue('dc01.corp.local')
  await expect(page.getByLabel('OU-uri de căutare')).toHaveValue(/OU=Externi/)
  await expect(page.getByLabel('Cont de citire din AD')).toHaveValue('')
  await expect(page.getByLabel('Cont de citire din AD')).toHaveAttribute('placeholder', /setat/)
  await expect(page.getByLabel('Parola contului')).toHaveAttribute('placeholder', /setat/)

  const raw = await (await request.get('http://localhost:5080/api/admin/settings', { headers: SYSTEM_ADMIN })).text()
  expect(raw).not.toContain('Parola-Secreta-1')
  expect(raw).not.toContain('svc-mdel')

  // Curățenie: fără server AD completat, aplicația de test revine la AD-ul simulat folosit de celelalte teste.
  const cleanup = await request.put('http://localhost:5080/api/admin/settings', {
    headers: SYSTEM_ADMIN,
    data: { values: { 'Ad.LdapServer': '', 'Ad.Port': '', 'Ad.UseSsl': 'false', 'Ad.SearchBases': '', 'Ad.ServiceUser': '', 'Ad.ServicePassword': '' } },
  })
  expect(cleanup.status()).toBe(204)
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

test('șablonul de email se editează în română, cu previzualizare', async ({ browser }) => {
  const page = await (await browser.newContext({ extraHTTPHeaders: ADMIN })).newPage()
  await page.goto('/admin')
  await page.getByRole('tab', { name: 'Șabloane email' }).click()
  await page.getByRole('option', { name: /Delegare respinsă/ }).click()
  await page.locator('#template-subject').fill('Cererea #{id} a fost respinsă')
  await page.locator('#template-body').fill('Cererea pentru {sursa} a fost respinsă.\nMotiv: ')
  await page.getByRole('button', { name: '{motiv}' }).click()
  await expect(page.frameLocator('iframe').getByText('Motiv: Revenire anticipată din concediu')).toBeVisible()
  await expect(page.getByText('Cererea #1024 a fost respinsă')).toBeVisible()
  await page.getByRole('button', { name: 'Salvează' }).click()
  await expect(page.getByText('Șablonul a fost salvat')).toBeVisible()

  // Câmp necunoscut: previzualizarea arată eroarea.
  await page.locator('#template-body').fill('Text cu {necunoscut}')
  await expect(page.getByText(/Unknown fields/).first()).toBeVisible()
  await page.getByRole('button', { name: 'Restabilește textul implicit' }).click()
  await expect(page.locator('#template-body')).toHaveValue('Cererea de forward a fost respinsă.')
})
