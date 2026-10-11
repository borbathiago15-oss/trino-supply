import { existsSync, readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import type { Page } from '@playwright/test';
import { totp } from './totp';

/** Onde o globalSetup deixa os tokens do login único da execução.
 *  Fora de `test-results`, que o Playwright limpa no começo de cada rodada. */
export const ARQUIVO_SESSAO = fileURLToPath(new URL('../.e2e/sessao.json', import.meta.url));

/**
 * O segredo do autenticador do administrador, gravado na primeira execução contra este banco:
 * o administrador não entra sem a segunda etapa, e a suíte faz o papel do telefone dele.
 */
export const ARQUIVO_MFA = fileURLToPath(new URL('../.e2e/mfa.json', import.meta.url));

/** O segredo do autenticador do administrador, se esta máquina já o cadastrou. */
export const segredoDoMfa = (): string | null =>
  existsSync(ARQUIVO_MFA) ? (JSON.parse(readFileSync(ARQUIVO_MFA, 'utf8')).secret as string) : null;

/**
 * A segunda etapa do login do administrador pela tela: a senha certa pede o código, e a suíte
 * faz o papel do telefone dele. Para a spec que entra pela tela em vez de `abrirAutenticado`.
 */
export async function segundaEtapaDoAdmin(page: Page) {
  const segredo = segredoDoMfa();
  if (!segredo) throw new Error('o cenário não guardou o segredo do autenticador do administrador (.e2e/mfa.json)');
  await page.getByTestId('segunda-etapa').waitFor();
  await page.locator('#codigo').fill(totp(segredo));
  await page.getByRole('button', { name: 'Confirmar' }).click();
}

/** Cenário criado por `npm run e2e:seed` nesta execução. */
export const ARQUIVO_CENARIO = fileURLToPath(new URL('../.e2e/cenario.json', import.meta.url));

export const lerCenario = (): { pedidoNumero: string; pedidoId: string } =>
  JSON.parse(readFileSync(ARQUIVO_CENARIO, 'utf8'));

let tokens: { accessToken: string; refreshToken: string } | null = null;

const lerTokens = () => (tokens ??= JSON.parse(readFileSync(ARQUIVO_SESSAO, 'utf8')));

/** O token do login único da execução, para a spec que precisa semear pela API. */
export const tokenDaSessao = () => lerTokens().accessToken;

/** Abre a página já autenticada, na mesma sessão que o legado usa. */
export async function abrirAutenticado(page: Page, destino: string) {
  const { accessToken, refreshToken } = lerTokens();
  await page.addInitScript(([a, r]) => {
    sessionStorage.setItem('ts.access', a);
    sessionStorage.setItem('ts.refresh', r);
  }, [accessToken, refreshToken] as const);
  await page.goto(destino);
}
