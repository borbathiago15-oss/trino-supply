import { expect, test } from '@playwright/test';
import { abrirAutenticado } from './sessao';

/**
 * OPS-D — o telefone serve para as duas coisas que se faz nele: aprovar e
 * acompanhar. O notebook segue sendo onde o trabalho acontece.
 *
 * Antes deste ajuste o menu de 252px ficava no fluxo e sobravam **138px** de
 * conteúdo numa tela de 390px, com a página rolando de lado. O que este teste
 * protege é isso: a casca não pode voltar a comer a tela.
 */
test.describe('Celular (390px)', () => {
  test.use({ viewport: { width: 390, height: 844 } });

  const semRolagemLateral = async (page: import('@playwright/test').Page) =>
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBe(390);

  test('o menu sai do caminho e volta pelo botão, e a página não rola de lado', async ({ page }) => {
    await abrirAutenticado(page, '/painel');
    await expect(page.locator('#titulo-pagina')).toBeVisible();
    await semRolagemLateral(page);

    // fora da tela, mas presente: quem precisa do menu chega nele pelo botão
    const menu = page.getByRole('link', { name: 'Central de Aprovação' });
    await expect(menu).not.toBeInViewport();

    await page.getByRole('button', { name: 'Abrir o menu' }).click();
    await expect(menu).toBeInViewport();

    // e escolher uma tela fecha a gaveta: quem tocou quer ver a tela, não o menu
    await menu.click();
    await expect(page).toHaveURL(/\/aprovacoes$/);
    await expect(menu).not.toBeInViewport();
    await semRolagemLateral(page);
  });

  test('acompanhar: a tela de solicitações cabe no telefone', async ({ page }) => {
    // sem afirmar sobre a tabela: num banco novo não há SC nenhuma e a tela mostra
    // o estado vazio. O que este teste protege é a largura, e o campo de busca —
    // por onde se acha a SC no telefone — existe com ou sem dado
    await abrirAutenticado(page, '/solicitacoes');
    await expect(page.getByLabel('Buscar')).toBeVisible();
    await semRolagemLateral(page);
  });

  test('no notebook o menu continua sendo a coluna de sempre', async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await abrirAutenticado(page, '/painel');
    await expect(page.getByRole('link', { name: 'Central de Aprovação' })).toBeInViewport();
    await expect(page.getByRole('button', { name: 'Abrir o menu' })).toBeHidden();
  });
});
