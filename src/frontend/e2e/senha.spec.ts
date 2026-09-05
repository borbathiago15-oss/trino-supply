import { expect, test } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { ARQUIVO_SESSAO } from './sessao';

const API = process.env.API_URL ?? 'http://127.0.0.1:5099';
const marca = Date.now().toString().slice(-6);
const EMAIL = `e2e.senha.${marca}@trino.test`;
const PROVISORIA = `Provisoria#${marca}!k`;
const DEFINITIVA = `Definitiva#${marca}!k`;

const tokenAdmin = () => JSON.parse(readFileSync(ARQUIVO_SESSAO, 'utf8')).accessToken as string;

test.describe('Primeiro acesso: senha provisória (SEC-004)', () => {
  test.beforeAll(async () => {
    const res = await fetch(`${API}/api/v1/users/`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Authorization: 'Bearer ' + tokenAdmin() },
      body: JSON.stringify({
        email: EMAIL, name: 'Pessoa do Teste', role: 'Requester',
        password: PROVISORIA, modules: ['SOLICITACOES'],
      }),
    });
    expect(res.status, await res.text()).toBe(201);
  });

  // o login é limitado a 10 tentativas por minuto por IP (SEC-003), então o
  // caminho inteiro do primeiro acesso cabe numa sessão só
  test('a senha do cadastro prende na troca, recusa a previsível e libera com a boa', async ({ page }) => {
    await page.goto('/login');
    await page.locator('#email').fill(EMAIL);
    await page.locator('#password').fill(PROVISORIA);
    await page.getByRole('button', { name: /Entrar/ }).click();

    await expect(page).toHaveURL(/\/trocar-senha$/);
    await expect(page.getByTestId('senha-provisoria')).toContainText('definida por quem cadastrou');
    // a casca da aplicação nem carrega antes da troca
    await expect(page.getByRole('complementary', { name: 'Menu' })).toBeHidden();

    // tentar outra rota pela URL volta para a troca
    await page.goto('/painel');
    await expect(page).toHaveURL(/\/trocar-senha$/);

    const salvar = page.getByRole('button', { name: 'Salvar nova senha' });
    await page.locator('#senha-atual').fill(PROVISORIA);

    // o nome do sistema é justamente o padrão que a regra existe para barrar
    await page.locator('#senha-nova').fill('TrinoSupply#2026');
    await page.locator('#senha-confirmacao').fill('TrinoSupply#2026');
    await expect(page.getByTestId('pendencias-senha')).toContainText('previsíveis');
    await expect(salvar).toBeDisabled();

    await page.locator('#senha-nova').fill(DEFINITIVA);
    await page.locator('#senha-confirmacao').fill(DEFINITIVA);
    await expect(salvar).toBeEnabled();
    await salvar.click();

    // troca feita: a sessão segue sem novo login e o app abre
    await expect(page).toHaveURL(/\/painel$/);
    await expect(page.getByRole('complementary', { name: 'Menu' })).toBeVisible();
  });

  test('a senha provisória não vale mais, e a nova entra direto no sistema', async ({ page }) => {
    const velha = await fetch(`${API}/api/v1/auth/login`, {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email: EMAIL, password: PROVISORIA }),
    });
    expect(velha.status).toBe(401);

    await page.goto('/login');
    await page.locator('#email').fill(EMAIL);
    await page.locator('#password').fill(DEFINITIVA);
    await page.getByRole('button', { name: /Entrar/ }).click();

    await expect(page).not.toHaveURL(/\/trocar-senha$/);
    await expect(page.getByRole('complementary', { name: 'Menu' })).toBeVisible();
  });
});
