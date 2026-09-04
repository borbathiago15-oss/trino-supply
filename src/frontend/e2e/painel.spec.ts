import { expect, test } from '@playwright/test';
import { abrirAutenticado } from './sessao';

test.describe('Dashboard de Suprimentos e Insights (React)', () => {
  test('o app abre no painel e a Central de Avisos leva à tela do aviso', async ({ page }) => {
    await abrirAutenticado(page, '/app');
    await expect(page).toHaveURL(/\/app\/painel$/);
    await expect(page.locator('#titulo-pagina')).toHaveText('Dashboard de Suprimentos');
    await expect(page.locator('body')).toContainText('Central de Avisos');

    // espera a leitura resolver antes de ramificar: com ou sem aviso, algo aparece
    const avisos = page.getByTestId('lista-avisos');
    await expect(avisos.or(page.getByText('Tudo em dia por aqui: nenhum aviso pendente. ✔'))).toBeVisible();
    if (await avisos.count()) {
      // todo aviso aponta para alguma tela — migrada (rota) ou clássica (deep link)
      const primeiro = avisos.locator('a').first();
      await expect(primeiro).toHaveAttribute('href', /^\/(#tela=)?[a-z-]/);
    } else {
      await expect(page.locator('body')).toContainText('Tudo em dia por aqui');
    }
  });

  test('painel: os KPIs, os gráficos e os rankings carregam', async ({ page }) => {
    await abrirAutenticado(page, '/app/painel');
    await expect(page.locator('body')).toContainText('Solicitações');
    await expect(page.locator('body')).toContainText('Tempo médio de aprovação');

    // os gráficos são SVG desenhados aqui: sem biblioteca externa
    await expect(page.getByRole('img', { name: 'Solicitações por mês, por situação' })).toBeVisible();
    await expect(page.getByRole('img', { name: 'Valor comprado por mês' })).toBeVisible();
    await expect(page.locator('body')).toContainText('Fornecedores por valor comprado');
    await expect(page.locator('body')).toContainText('Painel do comprador');
  });

  test('painel: o filtro só consulta ao aplicar, e limpar devolve o período inteiro', async ({ page }) => {
    await abrirAutenticado(page, '/app/painel');
    await expect(page.locator('#sd-cc')).toBeVisible();

    const opcoes = await page.locator('#sd-cc option').count();
    if (opcoes > 1) {
      await page.selectOption('#sd-cc', { index: 1 });
      const consulta = page.waitForResponse((r) => r.url().includes('analytics/supply?') && r.url().includes('costCenter='));
      await page.getByRole('button', { name: 'Aplicar filtros' }).click();
      await consulta;

      // limpar desfaz o filtro e refaz a apuração do período inteiro
      const limpeza = page.waitForResponse((r) =>
        r.url().includes('analytics/supply?') && !r.url().includes('costCenter='));
      await page.getByRole('button', { name: 'Limpar' }).click();
      await limpeza;
    } else {
      // sem filtro aplicado não há o que refazer: limpar só devolve os campos
      await page.getByRole('button', { name: 'Limpar' }).click();
    }
    await expect(page.locator('#sd-cc')).toHaveValue('');
  });

  test('insights: visão executiva, achados e backlog, com a janela trocando a leitura', async ({ page }) => {
    await abrirAutenticado(page, '/app/insights');
    await expect(page.locator('#titulo-pagina')).toHaveText('Insights & Executivo');
    await expect(page.locator('body')).toContainText('Spend (O.C.s)');
    await expect(page.locator('body')).toContainText('Compliance médio');

    const chamada = page.waitForResponse((r) => r.url().includes('analytics/insights?months=12'));
    await page.selectOption('#ins-meses', '12');
    await chamada;

    await expect(page.locator('body')).toContainText(/Nenhum achado no período|INS-/);
    await expect(page.locator('body')).toContainText('Backlog de demandas em aberto');
    await expect(page.locator('body')).toContainText('TCO por produto');
  });
});
