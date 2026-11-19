import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './e2e',
  timeout: 240_000,
  expect: { timeout: 12_000 },
  workers: 1,
  retries: 0,
  reporter: 'list',
  use: {
    ...devices['Desktop Chrome'],
    baseURL: process.env.ERP_E2E_BASE_URL ?? 'https://localhost:8443',
    launchOptions: { executablePath: process.env.ERP_E2E_CHROMIUM_EXECUTABLE },
    actionTimeout: 12_000,
    navigationTimeout: 20_000,
    ignoreHTTPSErrors: true, // The deployment uses a localhost development certificate.
  },
});
