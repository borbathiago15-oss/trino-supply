import { expect, test, type Page } from '@playwright/test';
import { abrirAutenticado } from './sessao';

/**
 * O painel e a visão da diretoria são lidos no computador, no tablet e no celular, e os três
 * precisam estar certos — não uma versão reduzida no telefone. Cada critério daqui roda nos três
 * tamanhos: sem isso, "funciona no celular" vira opinião.
 */
const TAMANHOS = [
  { nome: 'celular', width: 375, height: 812 },
  { nome: 'tablet', width: 768, height: 1024 },
  { nome: 'computador', width: 1440, height: 900 },
] as const;

const semRolagemLateral = async (page: Page, largura: number) =>
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(largura);

for (const t of TAMANHOS) {
  test.describe(`${t.nome} (${t.width}px)`, () => {
    test.use({ viewport: { width: t.width, height: t.height } });

    test('painel: filtros principais, cards e gráficos cabem sem rolar de lado', async ({ page }) => {
      await abrirAutenticado(page, '/painel');
      await expect(page.getByTestId('kpis-painel')).toBeVisible();
      await expect(page.getByTestId('filtros-principais').getByLabel('Empresa')).toBeVisible();
      await expect(page.getByRole('img', { name: 'Valor comprado por mês' })).toBeVisible();
      await semRolagemLateral(page, t.width);

      // abrir "Mais filtros" não pode empurrar a página para o lado
      await page.getByRole('button', { name: 'Mais filtros' }).click();
      await expect(page.getByTestId('mais-filtros')).toBeVisible();
      await semRolagemLateral(page, t.width);
    });

    test('diretoria: os números e a fila cabem sem rolar de lado', async ({ page }) => {
      await abrirAutenticado(page, '/diretoria');
      await expect(page.locator('#titulo-pagina')).toBeVisible();
      await expect(page.getByTestId('numero-gasto')).toBeVisible();
      await semRolagemLateral(page, t.width);
    });
  });
}

test('painel: os filtros de "Mais filtros" chegam ao servidor e voltam como chip', async ({ page }) => {
  await abrirAutenticado(page, '/painel');
  await page.getByRole('button', { name: 'Mais filtros' }).click();
  await page.selectOption('#sd-prioridade', 'URGENT');
  const consulta = page.waitForResponse((r) => r.url().includes('analytics/supply?') && r.url().includes('priority=URGENT'));
  await page.getByRole('button', { name: 'Aplicar filtros' }).click();
  expect((await consulta).status()).toBe(200);
  await expect(page.getByTestId('contador-filtros')).toHaveText('1');

  const semFiltro = page.waitForResponse((r) => r.url().includes('analytics/supply?') && !r.url().includes('priority='));
  await page.getByRole('button', { name: /Remover filtro Prioridade: Urgente/ }).click();
  expect((await semFiltro).status()).toBe(200);
});
