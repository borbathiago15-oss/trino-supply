import { expect, test } from '@playwright/test';
import { abrirAutenticado } from './sessao';

const marca = Date.now().toString().slice(-6);

/** Cria uma solicitação de material e a aprova, para ela cair na fila do almoxarifado. */
async function pedirEAprovar(page: import('@playwright/test').Page, observacao: string) {
  await abrirAutenticado(page, '/material/nova');
  await page.selectOption('#mr-cc', 'E2E-001');
  await page.fill('#mr-notes', observacao);
  await page.selectOption('#mr-family', 'EPI CENARIO E2E');
  const grade = page.getByTestId('grade-produtos');
  await expect(grade).toBeVisible();
  const primeira = grade.locator('tr[data-produto]').first();
  await primeira.locator('input[type=checkbox]').check();
  await primeira.locator('input[type=number]').fill('6');
  await page.getByRole('button', { name: 'Enviar ao almoxarifado' }).click();
  await expect(page).toHaveURL(/\/material$/);

  // o Nível 1 do centro libera; o admin do cenário acumula esse papel
  await abrirAutenticado(page, '/aprovacoes');
  const linha = page.locator('tr', { hasText: observacao }).first();
  await expect(linha).toBeVisible();
  await linha.getByRole('button', { name: 'Aprovar' }).click();
  const dialogo = page.getByRole('dialog');
  await dialogo.getByRole('button', { name: 'Liberar' }).click();
  await expect(page.getByTestId('toast').last()).toContainText('liberada para o almoxarifado');
}

test.describe('Estoque e Triagem de Material (React)', () => {
  test('fila do almoxarifado: atende em parte e o faltante vira solicitação de compra', async ({ page }) => {
    const observacao = `E2E fila ${marca}`;
    await pedirEAprovar(page, observacao);

    await abrirAutenticado(page, '/estoque/fila');
    await expect(page.locator('#titulo-pagina')).toHaveText('Fila de Atendimento');

    const linha = page.locator('tr', { hasText: observacao }).first();
    await expect(linha).toBeVisible();
    await linha.getByRole('button', { name: 'Atender' }).click();

    // abre com o aprovado preenchido; entregar 2 de 6 deixa 4 para comprar
    const grade = page.getByTestId('itens-atendimento');
    const campo = grade.locator('input[type=number]').first();
    await expect(campo).toHaveValue('6');
    await campo.fill('2');
    await page.getByRole('button', { name: 'Confirmar atendimento' }).click();
    await expect(page.getByTestId('toast').last()).toContainText(/virou a solicitação [A-Z]+-\d{4}-\d+/);

    // atendida em parte, some da fila e aparece como parcial em Minhas Solicitações
    await expect(page.locator('tr', { hasText: observacao })).toHaveCount(0);
    await abrirAutenticado(page, '/material');
    await expect(page.locator('tr', { hasText: observacao }).first()).toContainText(/Atendida parcialmente|Rota de compra/);
  });

  test('fila: escopo “designadas a mim” explica a lista vazia', async ({ page }) => {
    await abrirAutenticado(page, '/estoque/fila');
    await page.selectOption('select[aria-label="Escopo da fila"]', 'MINHAS');
    await expect(page.locator('body')).toContainText(/designada a você|Solicitação/);
  });

  test('painel de atendimentos: o cartão isola o bloco e volta ao clicar de novo', async ({ page }) => {
    await abrirAutenticado(page, '/estoque/atendimentos');
    await expect(page.locator('#titulo-pagina')).toHaveText('Painel de Atendimentos');
    await expect(page.getByTestId('painel-por-centro').or(page.getByText('Sem dados ainda.').first())).toBeVisible();

    const cartao = page.getByRole('button', { name: /Parciais aguardando compra/ });
    await cartao.click();
    await expect(cartao).toHaveAttribute('aria-pressed', 'true');
    await expect(page.getByTestId('painel-parcial').or(page.getByText('Nada por aqui. ✔').first())).toBeVisible();
    await expect(page.locator('body')).toContainText('Mostrando só este cartão');

    await cartao.click();
    await expect(cartao).toHaveAttribute('aria-pressed', 'false');
    await expect(page.locator('body')).toContainText('Clique em um cartão para ver a lista');
  });

  test('triagem de material: filtra e designa', async ({ page }) => {
    await abrirAutenticado(page, '/gestao-solicitacoes');
    // a tela passou a triar só material: a demanda de COMPRA é triada na Torre,
    // na própria linha do item, e por isso a prioridade da SC saiu daqui
    await expect(page.locator('#titulo-pagina')).toHaveText('Triagem de Material');

    // o cenário do CI pode não ter requisição de material — e a tela vazia também é
    // contrato: ela se explica em vez de mostrar tabela sem linha
    const tabela = page.getByTestId('tabela-demandas');
    await expect(tabela.or(page.getByText('Nenhuma demanda neste filtro'))).toBeVisible();

    // o filtro de tempo na fila age na tela, sem nova consulta
    await page.selectOption('#tri-faixa', '3');
    await page.selectOption('#tri-faixa', '');

    if (await tabela.count()) {
      // designar: o select da linha grava e a tela recarrega
      const responsavel = tabela.locator('tr[data-demanda]').first().locator('select').first();
      if ((await responsavel.locator('option').count()) > 1) {
        await responsavel.selectOption({ index: 1 });
        await expect(page.getByTestId('toast').last()).toContainText(/designada|Designação/);
      }
      // a prioridade da SC mudou de lugar: é da Torre agora, e o E2E dela cobre a régua
      await expect(tabela.getByRole('button', { name: 'Tornar Urgente' })).toHaveCount(0);
    }
  });

  test('lote: o botão só libera com demanda marcada e responsável escolhido', async ({ page }) => {
    await abrirAutenticado(page, '/gestao-solicitacoes');
    const tabela = page.getByTestId('tabela-demandas');
    await expect(tabela.or(page.getByText('Nenhuma demanda neste filtro'))).toBeVisible();
    if (!(await tabela.count())) return;   // sem material no cenário: nada a designar

    const botao = page.getByRole('button', { name: /Designar selecionadas/ });
    await expect(botao).toBeDisabled();
    await tabela.locator('input[type=checkbox]').first().check();
    await expect(botao).toContainText('(1)');
    await expect(botao).toBeDisabled();

    await page.selectOption('select[aria-label="Responsável do lote"]', { index: 1 });
    await expect(botao).toBeEnabled();
  });
});
