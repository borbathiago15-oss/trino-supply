import { existsSync } from 'node:fs';
import { defineConfig, devices } from '@playwright/test';

/**
 * Chromium: no CI o `playwright install` já provê o binário, então não se
 * aponta caminho nenhum; neste ambiente há um Chromium pré-instalado, que é
 * usado quando existe. Variável vazia conta como ausente — `??` deixaria
 * passar uma string vazia e o Playwright falharia ao abrir "".
 */
const CAMINHO_PADRAO = '/opt/pw-browsers/chromium';
const chromium = process.env.PLAYWRIGHT_CHROMIUM || (existsSync(CAMINHO_PADRAO) ? CAMINHO_PADRAO : undefined);

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
          ...(chromium ? { executablePath: chromium } : {}),
          args: ['--no-sandbox'],
        },
      },
    },
  ],
});
