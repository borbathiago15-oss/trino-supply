import { defineConfig, devices } from '@playwright/test';

/**
 * E2E do frontend. Os servidores (API e web) sobem fora daqui — o CI os inicia
 * no job, e localmente `npm run e2e` assume que já estão no ar. Rodar o Next em
 * modo produção é proposital: é o build que vai para o ar que queremos testar.
 */
export default defineConfig({
  testDir: './e2e',
  timeout: 60_000,
  expect: { timeout: 10_000 },
  fullyParallel: false,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL: process.env.WEB_URL ?? 'http://127.0.0.1:3002',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    locale: 'pt-BR',
  },
  projects: [
    {
      name: 'chromium',
      use: {
        ...devices['Desktop Chrome'],
        launchOptions: {
          executablePath: process.env.PLAYWRIGHT_CHROMIUM ?? '/opt/pw-browsers/chromium',
          args: ['--no-sandbox'],
        },
      },
    },
  ],
});
