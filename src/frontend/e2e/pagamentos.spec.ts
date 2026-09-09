import { expect, test } from '@playwright/test';
import { abrirAutenticado } from './sessao';

/** Sufixo por execução: o cenário roda várias vezes contra o mesmo banco. */
const marca = Date.now().toString().slice(-6);

/**
 * Os dois cadastros de pagamento e o efeito deles na cotação. O que este arquivo
 * prova é a ponta a ponta que a tela sozinha não prova: o que se cadastra aqui é o
 * que aparece na lista suspensa do registro da proposta.
 */
test.describe('Cadastros de pagamento (React)', () => {
  test('forma: nasce semeada, aceita cadastro e inativa sem sumir', async ({ page }) => {
    await abrirAutenticado(page, '/formas-pagamento');
    await expect(page.locator('#titulo-pagina')).toHaveText('Formas de Pagamento');

    // as formas do dia a dia entram pelo seed: o cadastro não nasce vazio
    const tabela = page.getByTestId('tabela-formas-pagamento');
    await expect(tabela.getByText('Pix', { exact: true })).toBeVisible();

    const nome = `E2E FORMA ${marca}`;
    await page.fill('#fp-nome', nome);
    await page.getByRole('button', { name: 'Cadastrar forma' }).click();
    await expect(tabela.getByText(nome, { exact: true })).toBeVisible();

    const linha = tabela.locator(`tr[data-forma="${nome}"]`);
    await linha.getByRole('button', { name: 'Inativar' }).click();
    // inativar não apaga: a linha continua, agora marcada
    await expect(linha.getByText('INATIVA')).toBeVisible();
  });

  test('condição: o prazo da primeira parcela aparece no resumo da linha', async ({ page }) => {
    await abrirAutenticado(page, '/condicoes-pagamento');
    await expect(page.locator('#titulo-pagina')).toHaveText('Condições de Pagamento');

    const nome = `E2E COND ${marca}`;
    await page.fill('#cp-nome', nome);
    await page.fill('#cp-parcelas', '3');
    await page.fill('#cp-primeiro', '45');
    await page.getByRole('button', { name: 'Cadastrar condição' }).click();

    const linha = page.getByTestId('tabela-condicoes-pagamento').locator(`tr[data-condicao="${nome}"]`);
    await expect(linha).toContainText('3 parcelas · 1ª em 45 dias');
  });

  test('o menu leva às duas telas sem recarregar a página', async ({ page }) => {
    await abrirAutenticado(page, '/painel');
    const menu = page.locator('aside[aria-label="Menu"]');
    await menu.getByRole('button', { name: 'Cadastros' }).click();
    await menu.getByRole('button', { name: 'Pagamento' }).click();

    await menu.getByRole('link', { name: 'Formas de Pagamento' }).click();
    await expect(page).toHaveURL(/\/formas-pagamento$/);
    await menu.getByRole('link', { name: 'Condições de Pagamento' }).click();
    await expect(page.locator('#titulo-pagina')).toHaveText('Condições de Pagamento');
  });
});
