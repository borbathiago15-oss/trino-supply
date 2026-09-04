import { existsSync } from 'node:fs';
import { defineConfig, devices } from '@playwright/test';

/**
 * E2E da tela React contra a API .NET real (que também serve o build em /app/).
 * Os servidores sobem fora daqui — o CI os inicia no job; localmente `npm run e2e`
 * assume a API em http://127.0.0.1:5099 com o build já feito (`npm run build`).
 */
const CAMINHO_PADRAO = '/opt/pw-browsers/chromium';
const chromium = process.env.PLAYWRIGHT_CHROMIUM || (existsSync(CAMINHO_PADRAO) ? CAMINHO_PADRAO : undefined);

export default defineConfig({
  testDir: './e2e',
  // um login por execução, compartilhado pelos workers (o login é limitado por IP)
  globalSetup: './e2e/global-setup.ts',
  timeout: 60_000,
  expect: { timeout: 10_000 },
  fullyParallel: false,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL: process.env.API_URL ?? 'http://127.0.0.1:5099',
    // uma espera por elemento que não existe falha com a mensagem certa, em vez
    // de consumir o timeout do teste inteiro e reportar só "Test timeout"
    actionTimeout: 15_000,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    locale: 'pt-BR',
  },
  projects: [
    {
      name: 'chromium',
      use: {
        ...devices['Desktop Chrome'],
        launchOptions: { ...(chromium ? { executablePath: chromium } : {}), args: ['--no-sandbox'] },
      },
    },
  ],
});
