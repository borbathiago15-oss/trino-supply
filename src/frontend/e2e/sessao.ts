import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import type { Page } from '@playwright/test';

/** Onde o globalSetup deixa os tokens do login único da execução.
 *  Fora de `test-results`, que o Playwright limpa no começo de cada rodada. */
export const ARQUIVO_SESSAO = fileURLToPath(new URL('../.e2e/sessao.json', import.meta.url));

/** Cenário criado por `npm run e2e:seed` nesta execução. */
export const ARQUIVO_CENARIO = fileURLToPath(new URL('../.e2e/cenario.json', import.meta.url));

export const lerCenario = (): { pedidoNumero: string; pedidoId: string } =>
  JSON.parse(readFileSync(ARQUIVO_CENARIO, 'utf8'));

let tokens: { accessToken: string; refreshToken: string } | null = null;

const lerTokens = () => (tokens ??= JSON.parse(readFileSync(ARQUIVO_SESSAO, 'utf8')));

/** Abre a página já autenticada, na mesma sessão que o legado usa. */
export async function abrirAutenticado(page: Page, destino: string) {
  const { accessToken, refreshToken } = lerTokens();
  await page.addInitScript(([a, r]) => {
    sessionStorage.setItem('ts.access', a);
    sessionStorage.setItem('ts.refresh', r);
  }, [accessToken, refreshToken] as const);
  await page.goto(destino);
}
