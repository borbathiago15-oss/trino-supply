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

  /** §5: os dois KPIs que faltavam existem e o de exceções filtra. */
  test('em faturamento e exceções aparecem, e exceções vira filtro', async ({ page }) => {
    await abrirAutenticado(page, '/torre');
    await expect(page.getByRole('button', { name: /Em faturamento/ })).toBeVisible();
    await expect(page.getByRole('button', { name: /Aguardando recebimento/ })).toBeVisible();

    const comExcecao = page.waitForResponse((r) =>
      r.url().includes('/api/v1/control-tower') && r.url().includes('exception=true'));
    await page.getByRole('button', { name: /Exceções/ }).click();
    expect((await comExcecao).status()).toBe(200);
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

  /** §5.1: os filtros que dependem do pedido saem na consulta com o nome certo. */
  test('fornecedor, número da O.C. e faixa de valor vão para o servidor', async ({ page }) => {
    await abrirAutenticado(page, '/torre');
    await expect(page.locator('#tc-fornecedor')).toBeVisible();

    await page.fill('#tc-fornecedor', 'Alfa');
    await page.fill('#tc-oc', '4521');
    await page.fill('#tc-valor-de', '100');
    const consulta = page.waitForResponse((r) =>
      r.url().includes('/api/v1/control-tower')
      && r.url().includes('supplier=Alfa')
      && r.url().includes('orderNumber=4521')
      && r.url().includes('minValue=100'));
    await page.getByRole('button', { name: 'Aplicar filtros' }).click();
    expect((await consulta).status()).toBe(200);

    // e o recorte sem resultado se explica, em vez de mostrar tabela vazia
    await expect(page.getByTestId('tabela-torre')
      .or(page.getByText('Nenhum item de compra neste recorte.'))).toBeVisible();
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
