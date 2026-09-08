import { expect, test } from '@playwright/test';
import { abrirAutenticado } from './sessao';

/**
 * Torre de Controle. O que só o E2E prova é que a paginação é de verdade —
 * que o servidor corta a página e devolve o total da base, em vez de a tela
 * fatiar uma lista que já veio inteira.
 */
test.describe('Torre de Controle (React)', () => {
  test('abre com os KPIs, a lista por item e o recorte na chamada', async ({ page }) => {
    const consulta = page.waitForResponse((r) => r.url().includes('/api/v1/control-tower'));
    await abrirAutenticado(page, '/torre');
    await expect(page.locator('#titulo-pagina')).toHaveText('Torre de Controle');
    expect((await consulta).status()).toBe(200);

    // os KPIs também são filtros: cada um é um botão
    await expect(page.getByRole('button', { name: /Em cotação/ })).toBeVisible();
    await expect(page.getByRole('button', { name: /Atrasados/ })).toBeVisible();

    // com ou sem dado, a tela se explica
    await expect(page.getByTestId('tabela-torre')
      .or(page.getByText('Nenhum item de compra neste recorte.'))).toBeVisible();
  });

  test('o KPI vira filtro e o pedido sai com a etapa', async ({ page }) => {
    await abrirAutenticado(page, '/torre');
    await expect(page.getByRole('button', { name: /Em cotação/ })).toBeVisible();

    const comEtapa = page.waitForResponse((r) =>
      r.url().includes('/api/v1/control-tower') && r.url().includes('stage=COTACAO'));
    await page.getByRole('button', { name: /Em cotação/ }).click();
    expect((await comEtapa).status()).toBe(200);
  });

  test('a paginação é do servidor: a página pedida vai na consulta', async ({ page }) => {
    await abrirAutenticado(page, '/torre');
    // esperar o painel de filtros não basta: ele renderiza antes de os dados
    // chegarem, e a contagem ainda não existiria quando o teste perguntasse
    await expect(page.getByTestId('tabela-torre')
      .or(page.getByText('Nenhum item de compra neste recorte.'))).toBeVisible();

    const contagem = page.getByTestId('torre-contagem');
    if (!(await contagem.count())) {
      // sem item no ambiente, a tela mostra o estado vazio — e isso já é o contrato
      await expect(page.getByText('Nenhum item de compra neste recorte.')).toBeVisible();
      return;
    }

    const proxima = page.getByRole('button', { name: 'Próxima' });
    if (await proxima.isEnabled()) {
      const pagina2 = page.waitForResponse((r) =>
        r.url().includes('/api/v1/control-tower') && r.url().includes('page=2'));
      await proxima.click();
      expect((await pagina2).status()).toBe(200);
      await expect(contagem).toContainText('página 2');
      await expect(page.getByRole('button', { name: 'Anterior' })).toBeEnabled();
    } else {
      // uma página só: "Próxima" tem de estar travada, não sumida
      await expect(proxima).toBeDisabled();
    }
  });

  test('filtrar por centro de custo refaz a consulta e limpar desfaz', async ({ page }) => {
    await abrirAutenticado(page, '/torre');
    await expect(page.locator('#tc-cc')).toBeVisible();

    if ((await page.locator('#tc-cc option').count()) > 1) {
      await page.selectOption('#tc-cc', { index: 1 });
      const comCc = page.waitForResponse((r) =>
        r.url().includes('/api/v1/control-tower') && r.url().includes('costCenter='));
      await page.getByRole('button', { name: 'Aplicar filtros' }).click();
      await comCc;

      const semCc = page.waitForResponse((r) =>
        r.url().includes('/api/v1/control-tower') && !r.url().includes('costCenter='));
      await page.getByRole('button', { name: 'Limpar' }).click();
      await semCc;
    } else {
      await page.getByRole('button', { name: 'Limpar' }).click();
    }
    await expect(page.locator('#tc-cc')).toHaveValue('');
  });
});
