import { defineConfig, devices } from '@playwright/test'

/**
 * Teste end-to-end: pornesc backend-ul real (bază in-memory, AD/Exchange/SMTP simulate, scheduler la 3 s)
 * și frontend-ul Vite. Rulare: `npm run e2e` (din frontend/).
 */
export default defineConfig({
  testDir: './e2e',
  globalSetup: './e2e/global-setup.ts',
  fullyParallel: false,
  workers: 1,
  timeout: 90_000,
  expect: { timeout: 15_000 },
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL: 'http://localhost:5173',
    locale: 'ro-RO',
    timezoneId: 'Europe/Bucharest',
    trace: 'off',
    ...devices['Desktop Chrome'],
    viewport: { width: 1360, height: 900 },
  },
  webServer: [
    {
      command: 'dotnet run --project ../backend/src/MDel.Api --launch-profile http',
      url: 'http://localhost:5080/api/health',
      timeout: 180_000,
      reuseExistingServer: !process.env.CI,
      env: {
        Database__UseInMemory: 'true',
        Scheduler__IntervalSeconds: '3',
        ASPNETCORE_ENVIRONMENT: 'Development',
      },
    },
    {
      command: 'npm run dev -- --port 5173 --strictPort',
      url: 'http://localhost:5173',
      timeout: 60_000,
      reuseExistingServer: !process.env.CI,
    },
  ],
})
