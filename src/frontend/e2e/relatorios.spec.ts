import { expect, test } from '@playwright/test';
import { abrirAutenticado } from './sessao';

/**
 * Relatórios — a tela que a diretoria abre. O que estes testes protegem é a
 * jornada inteira: chegar pelo menu, apertar o recorte e sair com o PDF na mão.
 * O conteúdo dos blocos é assunto dos testes de componente e do backend; aqui o
 * banco pode estar vazio, e a tela tem de continuar de pé.
 */
test.describe('Relatórios (React)', () => {
  test('a tela abre com os seis blocos e o recorte escrito na tela', async ({ page }) => {
    await abrirAutenticado(page, '/relatorios');
    await expect(page.locator('#titulo-pagina')).toHaveText('Relatórios');

    // os seis títulos são o contrato com quem lê: numerados e na mesma ordem
    for (const titulo of [
      '1. Compras por família', '2. Saving por comprador', '3. Concentração por fornecedor',
      '4. Peso das compras urgentes', '5. Entrega no prazo (OTIF) por fornecedor',
      '6. Compras sem O.C. do ERP',
    ])
      await expect(page.getByRole('heading', { name: titulo })).toBeVisible();

    await expect(page.getByTestId('recorte-aplicado')).toContainText('Empresa: todas');
    await expect(page.getByTestId('recorte-aplicado')).toContainText('Comprador: todos');
  });

  test('o filtro só consulta ao aplicar, e o recorte vai para a chamada', async ({ page }) => {
    await abrirAutenticado(page, '/relatorios');
    await expect(page.locator('#rel-cc')).toBeVisible();

    const opcoes = await page.locator('#rel-cc option').count();
    if (opcoes > 1) {
      await page.selectOption('#rel-cc', { index: 1 });
      const consulta = page.waitForResponse((r) =>
        r.url().includes('analytics/report?') && r.url().includes('costCenter='));
      await page.getByRole('button', { name: 'Aplicar filtros' }).click();
      await consulta;

      const limpeza = page.waitForResponse((r) =>
        r.url().includes('analytics/report') && !r.url().includes('costCenter='));
      await page.getByRole('button', { name: 'Limpar' }).click();
      await limpeza;
    } else {
      await page.getByRole('button', { name: 'Limpar' }).click();
    }
    await expect(page.locator('#rel-cc')).toHaveValue('');
  });

  test('exportar pede o PDF do mesmo recorte e o servidor devolve o arquivo', async ({ page }) => {
    await abrirAutenticado(page, '/relatorios');
    await expect(page.getByRole('button', { name: 'Exportar em PDF' })).toBeEnabled();

    const resposta = page.waitForResponse((r) => r.url().includes('analytics/report/pdf'));
    await page.getByRole('button', { name: 'Exportar em PDF' }).click();
    const pdf = await resposta;

    expect(pdf.status()).toBe(200);
    expect(pdf.headers()['content-type']).toContain('application/pdf');
    // o nome do arquivo carrega o período: é o que separa duas folhas na mesa.
    // Os bytes em si (o `%PDF` no início) são conferidos no teste de backend —
    // a página consome o corpo como Blob e o Playwright não o guarda para nós.
    expect(pdf.headers()['content-disposition'])
      .toMatch(/relatorio-compras-\d{4}-\d{2}-\d{2}_a_\d{4}-\d{2}-\d{2}\.pdf/);
  });
});
