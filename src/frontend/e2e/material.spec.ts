import { expect, test } from '@playwright/test';
import { abrirAutenticado } from './sessao';

const marca = Date.now().toString().slice(-6);

test.describe('Material, Contratos, Scorecard e Compliance (React)', () => {
  test('solicitar material: a grade só vem com a família e o envio exige quantidade', async ({ page }) => {
    await abrirAutenticado(page, '/material/nova');
    await expect(page.locator('#titulo-pagina')).toHaveText('Solicitar Material');

    // sem família escolhida não há o que marcar
    await expect(page.getByTestId('grade-produtos')).toHaveCount(0);
    // o centro, a família e o produto vêm do cenário desta execução
    await page.selectOption('#mr-cc', 'E2E-001');
    await page.fill('#mr-notes', `E2E material ${marca}`);
    await page.selectOption('#mr-family', 'EPI CENARIO E2E');

    const grade = page.getByTestId('grade-produtos');
    await expect(grade).toBeVisible();

    // marcado sem quantidade: a tela recusa e continua na mesma página
    const primeira = grade.locator('tr[data-produto]').first();
    await primeira.locator('input[type=checkbox]').check();
    await page.getByRole('button', { name: 'Enviar ao almoxarifado' }).click();
    await expect(page.getByTestId('toast')).toContainText('Informe a quantidade');
    await expect(page).toHaveURL(/\/material\/nova$/);

    // com a quantidade, a solicitação sai e a tela leva para a lista
    await primeira.locator('input[type=number]').fill('2');
    await page.getByRole('button', { name: 'Enviar ao almoxarifado' }).click();
    await expect(page).toHaveURL(/\/material$/);
    await expect(page.getByTestId('toast').last()).toContainText('enviada ao almoxarifado');

    const linha = page.locator('tr', { hasText: `E2E material ${marca}` }).first();
    await expect(linha).toBeVisible();
    await expect(linha).toContainText(/Aguardando/);
  });

  test('cancelar a solicitação de material exige o motivo', async ({ page }) => {
    await abrirAutenticado(page, '/material');
    const linha = page.locator('tr', { hasText: `E2E material ${marca}` }).first();
    await expect(linha).toBeVisible();

    await linha.getByRole('button', { name: 'Cancelar' }).click();
    const dialogo = page.getByRole('dialog');
    const confirmar = dialogo.getByRole('button', { name: 'Cancelar solicitação' });
    await expect(confirmar).toBeDisabled();

    await dialogo.getByLabel(/Motivo do cancelamento/).fill(`teste E2E ${marca}`);
    await confirmar.click();
    await expect(page.getByTestId('toast').last()).toContainText('cancelada');
    await expect(page.locator('tr', { hasText: `E2E material ${marca}` }).first()).toContainText('Cancelada');
  });

  test('contratos: os KPIs abrem e o pleito valida antes de gravar', async ({ page }) => {
    await abrirAutenticado(page, '/contratos');
    await expect(page.locator('#titulo-pagina')).toHaveText('Contratos');
    await expect(page.locator('body')).toContainText('Teto contratado');

    const tabela = page.getByTestId('tabela-contratos');
    if (await tabela.count()) {
      await tabela.getByRole('button', { name: 'Registrar reajuste' }).first().click();
      const dialogo = page.getByRole('dialog');
      await dialogo.getByLabel(/Percentual pleiteado/).fill('5');
      await dialogo.getByLabel(/Percentual fechado/).fill('9');
      await dialogo.getByRole('button', { name: 'Registrar reajuste' }).click();
      await expect(page.getByTestId('toast')).toContainText('não pode ser maior');
      await dialogo.getByRole('button', { name: 'Cancelar' }).click();
    } else {
      await expect(page.locator('body')).toContainText('Nenhum contrato de parceria cadastrado');
    }
  });

  test('scorecard: a janela de análise recarrega a tabela', async ({ page }) => {
    await abrirAutenticado(page, '/scorecard');
    await expect(page.locator('#titulo-pagina')).toHaveText('Scorecard de Fornecedores');
    await expect(page.locator('body')).toContainText('OTIF (peso 50)');

    const chamada = page.waitForResponse((r) => r.url().includes('supplier-scorecard?months=12'));
    await page.selectOption('#sc-meses', '12');
    await chamada;
    await expect(page.locator('body')).toContainText(/Fornecedor|Nenhum fornecedor com atividade/);
  });

  test('compliance: o painel mostra o score e as médias', async ({ page }) => {
    await abrirAutenticado(page, '/compliance');
    await expect(page.locator('#titulo-pagina')).toHaveText('Compliance');
    await expect(page.locator('body')).toContainText('Score médio');
    await expect(page.locator('body')).toContainText('Médias por comprador e por centro de custo');
    await expect(page.locator('body')).toContainText(/Processo|Nenhum processo de cotação para avaliar/);
  });
});
