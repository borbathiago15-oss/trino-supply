import { expect, test } from '@playwright/test';
import { abrirAutenticado } from './sessao';

/**
 * Manual e Suporte no topo de toda tela. O que só o E2E prova é o caminho que atravessa
 * telas: ler o manual da Torre, abrir o chamado dali sem digitar onde se estava, e achar o
 * chamado — com a tela de origem — na lista e na conversa, gravado de verdade.
 */
test.describe('Manual e Suporte', () => {
  test('o manual é o da tela aberta', async ({ page }) => {
    await abrirAutenticado(page, '/pedidos');
    await page.getByRole('button', { name: 'Manual', exact: true }).click();
    const gaveta = page.getByRole('dialog', { name: 'Manual — Pedidos de Compra (O.C.)' });
    await expect(gaveta).toBeVisible();
    await expect(gaveta.getByText('Passo a passo')).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(gaveta).toHaveCount(0);
  });

  test('do manual da Torre ao chamado gravado, com a tela de origem', async ({ page }) => {
    const assunto = `Dúvida sobre a Torre ${Date.now()}`;
    await abrirAutenticado(page, '/torre');

    await page.getByRole('button', { name: 'Manual', exact: true }).click();
    await page.getByRole('button', { name: 'Abrir chamado sobre esta tela' }).click();
    await expect(page.getByTestId('tela-do-chamado')).toHaveText('Torre de Controle');

    await page.getByLabel(/Assunto/).fill(assunto);
    await page.getByLabel(/O que aconteceu/).fill('Não entendi a diferença entre atrasado e prazo estourado.');
    await page.getByRole('button', { name: 'Abrir chamado' }).click();

    const numero = page.getByTestId('numero-do-chamado');
    await expect(numero).toHaveText(/^CH-\d{4}-\d{6}$/);
    await page.getByRole('link', { name: 'Ver o chamado' }).click();

    await expect(page).toHaveURL(/\/suporte\/[0-9a-f-]{36}$/);
    await expect(page.getByTestId('conversa')).toContainText('Não entendi a diferença');
    await expect(page.getByRole('link', { name: 'Torre de Controle' })).toHaveAttribute('href', '/torre');

    // a resposta de quem abriu entra na conversa
    await page.getByLabel('Responder').fill('Complementando: vi isso na linha do pedido 12.');
    await page.getByRole('button', { name: 'Enviar resposta' }).click();
    await expect(page.getByTestId('conversa')).toContainText('Complementando');

    // e aparece na lista de chamados, com a tela
    await page.getByRole('link', { name: '← Chamados de suporte' }).click();
    const linha = page.getByTestId('tabela-chamados').getByRole('row', { name: new RegExp(assunto) });
    await expect(linha).toContainText('Torre de Controle');
  });

  test('quem abriu encerra o próprio chamado', async ({ page }) => {
    await abrirAutenticado(page, '/painel');
    await page.getByRole('button', { name: 'Suporte', exact: true }).click();
    await page.getByLabel(/Assunto/).fill('Encerrar sozinho');
    await page.getByLabel(/O que aconteceu/).fill('Achei a resposta no manual, pode fechar.');
    await page.getByRole('button', { name: 'Abrir chamado' }).click();
    await page.getByRole('link', { name: 'Ver o chamado' }).click();

    await page.getByRole('button', { name: 'Encerrar chamado' }).click();
    await expect(page.getByTestId('a-vez-de')).toContainText('Resolvido');
    await expect(page.getByLabel('Responder (reabre o chamado)')).toBeVisible();
  });
});
