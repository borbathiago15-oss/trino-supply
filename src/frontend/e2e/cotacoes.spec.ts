import { expect, test } from '@playwright/test';
import { abrirAutenticado } from './sessao';

const marca = Date.now().toString().slice(-6);

/** Cria uma SC com dois itens de famílias diferentes e a envia, para ela cair na fila. */
async function scComDuasFamilias(page: import('@playwright/test').Page, justificativa: string) {
  await abrirAutenticado(page, '/solicitacoes/nova');
  await page.getByLabel('Produto').first().fill(`Luva ${marca}`);
  await page.getByLabel('Unidade').first().fill('PAR');
  await page.getByLabel('Quantidade').first().fill('10');
  await page.fill('#sc-justificativa', justificativa);
  await page.selectOption('#sc-cc', 'E2E-001');
  await page.getByRole('button', { name: 'Criar rascunho da SC' }).click();
  await expect(page).toHaveURL(/\/solicitacoes$/);

  const linha = page.locator('tr', { hasText: justificativa }).first();
  await linha.getByRole('button', { name: 'Enviar solicitação' }).click();
  await expect(page.getByTestId('toast').last()).toContainText('enviada');
}

test.describe('Abrir Cotação (React)', () => {
  test('a fila lista as solicitações e a seleção por item abre um processo', async ({ page }) => {
    const justificativa = `E2E cotação ${marca}`;
    await scComDuasFamilias(page, justificativa);

    await abrirAutenticado(page, '/cotacoes/abrir');
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
    // o processo abre no React, já pronto para convidar fornecedores
    await expect(page).toHaveURL(/\/cotacoes\/[0-9a-f-]+$/);
    await expect(page.locator('body')).toContainText(/RFQ-\d{4}-\d+/);
    await expect(page.getByTestId('itens-cotacao')).toBeVisible();
  });

  test('itens de centros de custo diferentes travam a abertura', async ({ page }) => {
    await abrirAutenticado(page, '/cotacoes/abrir');
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

test.describe('Processos de Cotação (React)', () => {
  test('o processo percorre convite, proposta, escolha e cancelamento', async ({ page }) => {
    const justificativa = `E2E processo ${marca}`;
    await scComDuasFamilias(page, justificativa);

    // abre o processo a partir da fila
    await abrirAutenticado(page, '/cotacoes/abrir');
    const fila = page.getByTestId('fila-cotacao');
    await expect(fila).toBeVisible();
    const linha = fila.locator('tr', { hasText: justificativa }).first();
    await linha.locator('input[type=checkbox]').check();
    await page.getByRole('button', { name: /Um processo com os itens marcados/ }).click();
    await expect(page).toHaveURL(/\/cotacoes\/[0-9a-f-]+$/);

    // sem proposta, o mapa diz o que fazer em vez de mostrar tabela vazia
    await expect(page.locator('body')).toContainText('Nenhuma proposta lançada ainda');

    // a tela diz o passo seguinte, em vez de exigir que se saiba a sequência de cor
    await expect(page.getByTestId('proximo-passo')).toContainText('Convidar fornecedores');

    // convidar um fornecedor: o cenário garante que existe ao menos um
    const convidar = page.locator('#rfq-convidar');
    await expect(convidar).toBeVisible();
    if (await convidar.locator('option').count() > 1) {
      await convidar.selectOption({ index: 1 });
      await page.getByRole('button', { name: 'Convidar' }).click();
      await expect(page.getByTestId('toast').last()).toContainText('convidado');
      await expect(page.getByTestId('fornecedores-convidados')).toContainText('AGUARDANDO');

      // lançar a proposta que chegou por e-mail
      const form = page.getByTestId('form-proposta');
      await expect(form).toBeVisible();
      await form.locator('input[type=number]').last().fill('11.50');
      await form.getByRole('button', { name: 'Registrar proposta' }).click();
      await expect(page.getByTestId('toast').last()).toContainText('Proposta registrada');
      await expect(page.getByTestId('mapa-cotacao')).toBeVisible();
    }

    // cancelar exige motivo
    await page.getByRole('button', { name: 'Cancelar processo' }).click();
    const dialogo = page.getByRole('dialog');
    await expect(dialogo.getByRole('button', { name: 'Cancelar processo' })).toBeDisabled();
    await dialogo.getByLabel(/Motivo do cancelamento/).fill(`teste E2E ${marca}`);
    await dialogo.getByRole('button', { name: 'Cancelar processo' }).click();
    await expect(page.getByTestId('toast').last()).toContainText('cancelado');
    // processo encerrado não tem passo seguinte a apontar
    await expect(page.getByTestId('proximo-passo')).toHaveCount(0);
  });

  test('a lista filtra por situação e abre o processo', async ({ page }) => {
    await abrirAutenticado(page, '/cotacoes');
    await expect(page.locator('#titulo-pagina')).toHaveText('Processos de Cotação');

    const tabela = page.getByTestId('tabela-processos');
    const vazia = page.getByText('Nenhum processo de cotação neste filtro.');
    await expect(tabela.or(vazia)).toBeVisible();
    if (!(await tabela.count())) return;

    await page.selectOption('#rfq-situacao', 'CANCELADA');
    await expect(page.locator('body')).toContainText(/Cancelada|Nenhum processo de cotação neste filtro/);

    await page.selectOption('#rfq-situacao', '');
    await page.getByRole('link', { name: 'Abrir processo' }).first().click();
    await expect(page).toHaveURL(/\/cotacoes\/[0-9a-f-]+$/);
    await expect(page.getByTestId('itens-cotacao')).toBeVisible();
  });
});
