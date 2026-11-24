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
  // Așteptăm lista filtrată, altfel primul rând poate fi încă din lista nefiltrată (cursă sub încărcare).
  const filtered = page.waitForResponse((r) => r.url().includes('/api/audit?') && r.url().includes('action=Settings'))
  await page.locator('#audit-action').selectOption('Settings.')
  await filtered
  await page.locator('tbody tr').first().click()
  const dialog = page.getByRole('dialog')
  // Eticheta lizibilă a câmpului (codul rămâne dedesubt) și valoarea nouă.
  await expect(dialog.getByRole('cell', { name: /Retenție audit/ })).toContainText('Retention.AuditDays')
  await expect(dialog.getByRole('cell', { name: '400', exact: true })).toBeVisible()
})

test('auditul arată în listă valorile introduse și, la modificare, vechi → nou', async ({ browser, request }) => {
  // Interval aleatoriu: o reluare a testului nu se suprapune cu încercarea anterioară pe aceeași căsuță sursă.
  const offsetDays = 3 + Math.floor(Math.random() * 300)
  const start = new Date(Date.now() + offsetDays * 86_400_000).toISOString()
  const end = new Date(Date.now() + (offsetDays + 1) * 86_400_000).toISOString()
  const created = await request.post('http://localhost:5080/api/delegations', {
    headers: ADMIN,
    data: { fromSam: 'maria.ionescu', toSam: 'ion.popescu', startUtc: start, endUtc: end, requiresApproval: true, notes: 'Concediu odihnă ZX-4471' },
  })
  expect(created.status()).toBe(201)
  const { id, rowVersion } = await created.json()
  const updated = await request.put(`http://localhost:5080/api/delegations/${id}`, {
    headers: ADMIN,
    data: { fromSam: 'maria.ionescu', toSam: 'ion.popescu', startUtc: start, endUtc: end, requiresApproval: true, notes: 'Concediu prelungit', rowVersion },
  })
  expect(updated.status()).toBe(200)

  const page = await (await browser.newContext({ extraHTTPHeaders: ADMIN })).newPage()
  await page.goto(`/audit?entityType=Delegation&entityId=${id}`)

  // Creare: câmpurile introduse, direct în listă (fără apeluri HTTP tehnice).
  const createdRow = page.locator('tbody tr', { hasText: 'Delegare creată' })
  await expect(createdRow).toContainText('De la (sursă): maria.ionescu')
  await expect(createdRow).toContainText('Către (destinatar): ion.popescu')
  await expect(createdRow).toContainText('Observații: Concediu odihnă ZX-4471')

  // Modificare: doar ce s-a schimbat, vechi → nou.
  const updatedRow = page.locator('tbody tr', { hasText: 'Delegare modificată' })
  await expect(updatedRow).toContainText('Observații: Concediu odihnă ZX-4471 → Concediu prelungit')
  await expect(page.locator('tbody tr', { hasText: 'Cerere HTTP' })).toHaveCount(0)

  // Apelurile tehnice reușite apar doar la cerere.
  // Starea se schimbă după navigarea din URL (filtrele stau în URL), deci se așteaptă, nu se verifică imediat.
  await page.getByLabel('Arată și apelurile tehnice reușite (HTTP)').click()
  await expect(page).toHaveURL(/includeHttp=true/)
  await expect(page.getByLabel('Arată și apelurile tehnice reușite (HTTP)')).toBeChecked()

  // Fără filtrul pe delegare (apelurile HTTP nu au țintă), apelurile tehnice apar doar cu comutatorul activ.
  await page.goto('/audit')
  await expect(page.locator('tbody tr', { hasText: 'Delegare creată' }).first()).toBeVisible()
  // (apelurile refuzate sau eșuate rămân vizibile mereu; aici contează doar cele reușite)
  const httpOk = page.locator('tbody tr').filter({ hasText: 'Cerere HTTP' }).filter({ hasText: 'Reușit' })
  await expect(httpOk).toHaveCount(0)
  await page.goto('/audit?includeHttp=true')
  await expect(httpOk.first()).toBeVisible()

  // Căutarea găsește și după valoarea introdusă.
  await page.goto('/audit')
  await page.getByLabel('Text').fill('ZX-4471')
  await expect(page.locator('tbody tr', { hasText: 'Delegare creată' })).toBeVisible()
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
