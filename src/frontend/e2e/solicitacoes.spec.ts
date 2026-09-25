import { expect, test } from '@playwright/test';
import { itemForaDoCatalogo } from './sc';
import { abrirAutenticado } from './sessao';

const marca = Date.now().toString().slice(-6);

test.describe('Solicitações de Compra (React)', () => {
  test('SC avulsa: cria rascunho, edita, envia e some das ações', async ({ page }) => {
    await abrirAutenticado(page, '/solicitacoes/nova');
    const justificativa = `E2E SC ${marca}`;

    // um item fora do catálogo: sai da busca, pelo "não achou?"
    await itemForaDoCatalogo(page, `Item avulso ${marca}`, '3', 'UN');

    await page.fill('#sc-justificativa', justificativa);
    await page.selectOption('#sc-cc', { index: 1 });
    await page.getByRole('radio', { name: /^Compra/ }).check();
    await page.getByRole('button', { name: 'Criar rascunho da SC' }).click();

    // ao criar, a tela leva para Minhas Solicitações (SC)
    await expect(page).toHaveURL(/\/solicitacoes$/);
    await expect(page.getByTestId('toast')).toContainText('criada como rascunho');

    const linha = page.locator('tr', { hasText: justificativa }).first();
    await expect(linha).toContainText('Rascunho');
    await expect(linha).toContainText(`3× Item avulso ${marca}`);

    // editar antes de enviar
    await linha.getByRole('button', { name: 'Editar' }).click();
    await page.fill('#sc-edit-justificativa', `${justificativa} revisada`);
    await page.getByRole('button', { name: 'Salvar alterações' }).click();
    await expect(page.getByTestId('toast').last()).toContainText('Solicitação atualizada.');
    const revisada = page.locator('tr', { hasText: `${justificativa} revisada` }).first();
    await expect(revisada).toBeVisible();

    // enviar: sai de rascunho e passa a mostrar o andamento
    await revisada.getByRole('button', { name: 'Enviar solicitação' }).click();
    await expect(page.getByTestId('toast').last()).toContainText('enviada');
    // a situação exibida é a do processo quando a API a calcula ("Pendente"),
    // e não mais o rótulo bruto de rascunho
    await expect(revisada).not.toContainText('Rascunho');
    await expect(revisada).toContainText('aguardando a designação');
    // e a linha do tempo do solicitante: enviada, e agora com o comprador
    const tempo = revisada.getByTestId('linha-do-tempo-sc');
    await expect(tempo.locator('[data-etapa="enviada"]')).toHaveAttribute('data-situacao', 'feita');
    await expect(tempo.locator('[data-etapa="comprador"]')).toHaveAttribute('data-situacao', 'atual');
    await expect(revisada.getByRole('button', { name: 'Enviar solicitação' })).toHaveCount(0);
  });

  test('urgência: os campos aparecem e são exigidos quando a prioridade é urgente', async ({ page }) => {
    await abrirAutenticado(page, '/solicitacoes/nova');
    await expect(page.locator('#sc-urg-motivo')).toHaveCount(0);
    await page.selectOption('#sc-prioridade', 'URGENT');
    await expect(page.locator('#sc-urg-motivo')).toBeVisible();
    await expect(page.locator('#sc-urg-motivo')).toHaveAttribute('required', '');
    await page.selectOption('#sc-prioridade', 'NORMAL');
    await expect(page.locator('#sc-urg-motivo')).toHaveCount(0);
  });

  test('SC em lote: exige quantidade e gera a SC pela grade', async ({ page }) => {
    await abrirAutenticado(page, '/solicitacoes/lote');
    const justificativa = `E2E lote ${marca}`;

    await page.getByRole('radio', { name: /^Compra/ }).check();
    await page.fill('#lote-justificativa', justificativa);
    await page.selectOption('#lote-cc', { index: 1 });
    await expect(page.getByTestId('grade-lote')).toBeVisible();

    // sem quantidade nenhuma, a tela recusa e não chama a API
    await page.getByRole('button', { name: 'Gerar SC com as quantidades informadas' }).click();
    await expect(page.getByTestId('toast')).toContainText('Qtd. a Solicitar');
    await expect(page).toHaveURL(/\/solicitacoes\/lote$/);

    const primeira = page.getByTestId('grade-lote').locator('tr[data-produto]').first();
    await primeira.locator('input[type=number]').fill('4');
    await page.getByRole('button', { name: 'Gerar SC com as quantidades informadas' }).click();
    await expect(page).toHaveURL(/\/solicitacoes$/);

    const linha = page.locator('tr', { hasText: justificativa }).first();
    await expect(linha).toContainText('lote');
    await expect(linha).toContainText('Rascunho');
  });

  test('excluir rascunho pede confirmação', async ({ page }) => {
    await abrirAutenticado(page, '/solicitacoes/nova');
    const justificativa = `E2E descartável ${marca}`;
    await itemForaDoCatalogo(page, `Item descartável ${marca}`, '1');
    await page.fill('#sc-justificativa', justificativa);
    await page.selectOption('#sc-cc', { index: 1 });
    await page.getByRole('radio', { name: /^Compra/ }).check();
    await page.getByRole('button', { name: 'Criar rascunho da SC' }).click();
    await expect(page).toHaveURL(/\/solicitacoes$/);

    const linha = page.locator('tr', { hasText: justificativa }).first();
    await linha.getByRole('button', { name: 'Excluir' }).click();
    const dialogo = page.getByRole('dialog');
    await expect(dialogo).toContainText('não pode ser desfeito');
    await dialogo.getByRole('button', { name: 'Excluir' }).click();
    await expect(page.getByTestId('toast').last()).toContainText('Rascunho excluído.');
    await expect(page.locator('tr', { hasText: justificativa })).toHaveCount(0);
  });

  test('central de aprovação: as filas carregam e o processo abre no React', async ({ page }) => {
    await abrirAutenticado(page, '/aprovacoes');
    await expect(page.locator('#titulo-pagina')).toHaveText('Central de Aprovação');
    // a tela sempre responde: ou lista processos, ou diz que não há nenhum
    await expect(page.locator('body')).toContainText(/aguardando a sua decisão|Nenhuma aprovação pendente/);

    // a decisão se toma no card; o processo completo é o segundo caminho
    const fila = page.getByTestId('fila-decisao');
    if (await fila.count()) {
      const card = fila.locator('li').first();
      await expect(card.getByTestId('valor-da-compra')).toBeVisible();
      await expect(card.getByRole('button', { name: 'Aprovar' })).toBeVisible();
      const link = card.getByRole('link', { name: /Ver processo completo/ });
      await expect(link).toHaveAttribute('href', /\/cotacoes\//);
      await link.click();
      // o processo agora abre no próprio React
      await expect(page).toHaveURL(/\/cotacoes\/[0-9a-f-]+$/);
      await expect(page.locator('#titulo-pagina')).toHaveText('Processos de Cotação');
    }
  });
});
