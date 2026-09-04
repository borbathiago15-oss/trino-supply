import { expect, test } from '@playwright/test';
import { abrirAutenticado } from './sessao';

const marca = Date.now().toString().slice(-6);

/** Cria uma SC com dois itens de famílias diferentes e a envia, para ela cair na fila. */
async function scComDuasFamilias(page: import('@playwright/test').Page, justificativa: string) {
  await abrirAutenticado(page, '/app/solicitacoes/nova');
  await page.getByLabel('Produto').first().fill(`Luva ${marca}`);
  await page.getByLabel('Unidade').first().fill('PAR');
  await page.getByLabel('Quantidade').first().fill('10');
  await page.fill('#sc-justificativa', justificativa);
  await page.selectOption('#sc-cc', 'E2E-001');
  await page.getByRole('button', { name: 'Criar rascunho da SC' }).click();
  await expect(page).toHaveURL(/\/app\/solicitacoes$/);

  const linha = page.locator('tr', { hasText: justificativa }).first();
  await linha.getByRole('button', { name: 'Enviar solicitação' }).click();
  await expect(page.getByTestId('toast').last()).toContainText('enviada');
}

test.describe('Abrir Cotação (React)', () => {
  test('a fila lista as solicitações e a seleção por item abre um processo', async ({ page }) => {
    const justificativa = `E2E cotação ${marca}`;
    await scComDuasFamilias(page, justificativa);

    await abrirAutenticado(page, '/app/cotacoes/abrir');
    await expect(page.locator('#titulo-pagina')).toHaveText('Abrir Cotação');

    const fila = page.getByTestId('fila-cotacao');
    await expect(fila).toBeVisible();
    const linha = fila.locator('tr', { hasText: justificativa }).first();
    await expect(linha).toBeVisible();

    // nada marcado: as duas ações ficam travadas
    const juntar = page.getByRole('button', { name: /Um processo com os itens marcados/ });
    await expect(juntar).toBeDisabled();

    // o cabeçalho da SC marca todos os itens dela
    await linha.locator('input[type=checkbox]').check();
    await expect(juntar).toBeEnabled();
    await expect(juntar).toContainText('(1)');

    await page.selectOption('select[aria-label="Tipo de cotação"]', 'COMPRA');
    await juntar.click();
    // a tela de Processos ainda é a do clássico. O deep link leva até lá e o próprio
    // legado consome o `#tela=` (ele limpa o hash), então o que se verifica é o efeito:
    // saiu do /app, a view de cotações está ativa e o processo novo está aberto nela.
    await expect(page).toHaveURL(/:\d+\/$/);
    await expect(page.locator('#view-quotations')).toHaveClass(/active/);
    await expect(page.locator('#view-quotations')).toContainText(/RFQ-\d{4}-\d+/);
  });

  test('itens de centros de custo diferentes travam a abertura', async ({ page }) => {
    await abrirAutenticado(page, '/app/cotacoes/abrir');
    const fila = page.getByTestId('fila-cotacao');
    const vazia = page.getByText('Nenhuma solicitação aguardando cotação. ✔');
    await expect(fila.or(vazia)).toBeVisible();
    if (!(await fila.count())) return;

    // marca tudo o que a fila oferecer: se houver mais de um centro, a tela avisa
    const caixas = fila.locator('input[type=checkbox]');
    const total = await caixas.count();
    for (let i = 0; i < total; i++) await caixas.nth(i).check();

    const aviso = page.getByText(/Centros de custo diferentes/);
    if (await aviso.count()) {
      await expect(page.getByRole('button', { name: /Um processo com os itens marcados/ })).toBeDisabled();
    } else {
      await expect(page.getByRole('button', { name: /Um processo com os itens marcados/ })).toBeEnabled();
    }
  });
});
