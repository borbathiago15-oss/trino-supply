import { expect, test } from '@playwright/test';
import { abrirAutenticado } from './sessao';

/** Sufixo por execução: o cenário roda várias vezes contra o mesmo banco. */
const marca = Date.now().toString().slice(-6);

test.describe('Cadastros (React)', () => {
  test('família: cadastra com prazos-meta, edita e inativa', async ({ page }) => {
    await abrirAutenticado(page, '/familias');
    const nome = `E2E FAMILIA ${marca}`;

    await page.fill('#fam-nome', nome);
    await page.fill('#fam-observacao', 'criada pelo teste de ponta a ponta');
    await page.fill('#fam-categoria', 'MRO');
    await page.fill('#fam-l1', '2');
    await page.fill('#fam-l2', '3');
    await page.fill('#fam-l3', '1');
    await page.fill('#fam-l4', '15');
    await page.getByRole('button', { name: 'Cadastrar família' }).click();
    await expect(page.getByTestId('toast')).toContainText('Família cadastrada.');

    const linha = page.locator(`tr[data-familia="${nome}"]`);
    await expect(linha).toContainText('MRO');
    await expect(linha).toContainText('2 + 3 + 1 + 15 = 21');
    await expect(linha).toContainText('ATIVA');

    // editar: mudar a meta da última etapa
    await linha.getByRole('button', { name: 'Editar' }).click();
    await expect(page.getByRole('heading', { name: `Editar família — ${nome}` })).toBeVisible();
    await page.fill('#fam-l4', '20');
    await page.getByRole('button', { name: 'Salvar alterações' }).click();
    await expect(linha).toContainText('2 + 3 + 1 + 20 = 26');

    await linha.getByRole('button', { name: 'Inativar' }).click();
    await expect(linha).toContainText('INATIVA');
    await linha.getByRole('button', { name: 'Reativar' }).click();
    await expect(linha).toContainText('ATIVA');
  });

  test('fornecedor: cadastra, homologa e gera a chave do portal', async ({ page }) => {
    await abrirAutenticado(page, '/fornecedores');
    const cnpj = `${marca}00000199`; // 14 dígitos, como a API exige
    const razao = `E2E Fornecedor ${marca} LTDA`;

    await page.fill('#forn-razao', razao);
    await page.fill('#forn-cnpj', cnpj);
    await page.fill('#forn-telefone', '(81) 3333-1000');
    await page.fill('#forn-fantasia', 'E2E Suprimentos');
    await page.fill('#forn-email', 'contato@e2e.com.br');
    await page.getByRole('button', { name: 'Cadastrar fornecedor' }).click();
    await expect(page.getByTestId('toast')).toContainText('Fornecedor cadastrado.');

    // busca antes de conferir a linha: a lista traz 50 por página e num banco já
    // usado o fornecedor recém-criado pode cair fora da primeira. A busca por CNPJ
    // é justamente o que precisa funcionar, então ela vem primeiro
    await page.getByLabel('Buscar').fill(cnpj);
    await expect(page.getByTestId('tabela-fornecedores').locator('tr[data-fornecedor]')).toHaveCount(1);

    const linha = page.locator(`tr[data-fornecedor="${cnpj}"]`);
    await expect(linha).toContainText('E2E Suprimentos');
    await expect(linha).toContainText('sem contrato');

    // homologação: novo fornecedor entra como prospect e vira homologado
    await linha.getByRole('button', { name: 'Homologação' }).click();
    const painel = page.locator('#homologacao');
    await expect(painel).toContainText(razao);
    await painel.locator('#hom-situacao').selectOption('HOMOLOGADO');
    await painel.getByRole('button', { name: 'Salvar situação' }).click();
    await expect(page.getByTestId('toast').last()).toContainText('Situação de homologação salva.');
    await expect(linha).toContainText('Homologado');

    // chave do portal: só depois de confirmar, e some ao fechar
    await linha.getByRole('button', { name: /Mais ações de/ }).click();
    await page.getByRole('menuitem', { name: 'Chave do portal' }).click();
    await page.getByRole('button', { name: 'Gerar nova chave' }).click();
    await expect(page.getByTestId('chave-portal')).not.toBeEmpty();
    await page.getByRole('dialog').getByRole('button', { name: 'Fechar' }).click();
    await expect(page.getByTestId('chave-portal')).toHaveCount(0);

    await linha.getByRole('button', { name: /Mais ações de/ }).click();
    await page.getByRole('menuitem', { name: 'Inativar' }).click();
    await expect(linha).toContainText('INATIVO');
  });

  test('centro de custo: cadastra com alçadas e o código sai da regional', async ({ page }) => {
    await abrirAutenticado(page, '/centros-custo');
    const nome = `E2E Centro ${marca}`;

    await page.fill('#cc-nome', nome);
    await page.fill('#cc-regional', 'BAHIA');
    await page.fill('#cc-cliente', 'Cliente E2E');
    await page.fill('#cc-limite1', '50000');
    // marca o primeiro aprovador de nível 1, se houver algum no ambiente
    const caixas = page.locator('input[type=checkbox]');
    if (await caixas.count()) await caixas.first().check();
    await page.getByRole('button', { name: 'Cadastrar centro de custo' }).click();
    await expect(page.getByTestId('toast')).toContainText('Centro de custo cadastrado.');

    const linha = page.locator('tr', { hasText: nome }).first();
    await expect(linha).toContainText('BAHIA');
    await expect(linha).toContainText('Cliente E2E');
    await expect(linha).toContainText('N1 R$');
    await expect(linha.locator('td').first()).toContainText('BAH-');
  });

  /**
   * §7 — o pré-cadastro atravessa a tela: entra sem CNPJ, aparece marcado como tal,
   * e o documento entra na edição. É o caminho de quem foi cotado antes de existir
   * cadastro e depois ganhou o BID.
   */
  test('fornecedor: pré-cadastro sem CNPJ e o documento completado na edição', async ({ page }) => {
    await abrirAutenticado(page, '/fornecedores');
    const razao = `E2E Pre Cadastro ${marca} LTDA`;
    const cnpj = `${marca}00000377`;

    await page.fill('#forn-razao', razao);
    await page.fill('#forn-telefone', '(81) 98888-1234');
    await page.getByRole('button', { name: 'Cadastrar fornecedor' }).click();
    await expect(page.getByTestId('toast')).toContainText('Fornecedor cadastrado.');

    await page.getByLabel('Buscar').fill(razao);
    const linha = page.getByTestId('tabela-fornecedores').locator('tr', { hasText: razao }).first();
    await expect(linha).toContainText('pré-cadastro');
    await expect(linha).toContainText('Prospect');

    await linha.getByRole('button', { name: 'Editar' }).click();
    await expect(page.locator('#forn-cnpj')).toBeEnabled();   // falta documento: dá para informar
    await page.fill('#forn-cnpj', cnpj);
    await page.getByRole('button', { name: 'Salvar alterações' }).click();
    await expect(page.getByTestId('toast').last()).toContainText('Cadastro completado');

    await page.getByLabel('Buscar').fill(cnpj);
    await expect(page.locator(`tr[data-fornecedor="${cnpj}"]`)).toContainText(razao);
    // gravado, o documento vira identidade e a edição não o troca mais
    await page.locator(`tr[data-fornecedor="${cnpj}"]`).getByRole('button', { name: 'Editar' }).click();
    await expect(page.locator('#forn-cnpj')).toBeDisabled();
  });

  test('contrato de parceria: fixa preço de um produto e depois encerra', async ({ page }) => {
    await abrirAutenticado(page, '/fornecedores');
    const cnpj = `${marca}00000280`;
    await page.fill('#forn-razao', `E2E Contrato ${marca} LTDA`);
    await page.fill('#forn-cnpj', cnpj);
    await page.fill('#forn-telefone', '(81) 3333-2000');
    await page.getByRole('button', { name: 'Cadastrar fornecedor' }).click();
    await expect(page.getByTestId('toast')).toContainText('Fornecedor cadastrado.');

    const linha = page.locator(`tr[data-fornecedor="${cnpj}"]`);
    await linha.getByRole('button', { name: /Mais ações de/ }).click();
    await page.getByRole('menuitem', { name: 'Contrato de parceria' }).click();
    const painel = page.locator('#contrato');
    await expect(painel).toBeVisible();

    const produtos = painel.getByRole('combobox', { name: 'Produto do contrato' }).first();
    const opcoes = await produtos.locator('option').count();
    test.skip(opcoes < 2, 'ambiente sem produtos no catálogo para contratar');

    await produtos.selectOption({ index: 1 });
    await painel.getByRole('spinbutton', { name: 'Preço fixo' }).first().fill('19.90');
    await painel.getByRole('spinbutton', { name: 'Prazo de entrega em dias' }).first().fill('7');
    await painel.locator('#ct-numero').fill(`CT-E2E-${marca}`);
    await painel.locator('#ct-inicio').fill('2026-01-01');
    await painel.locator('#ct-fim').fill('2030-12-31');
    await painel.getByRole('button', { name: 'Salvar contrato' }).click();
    await expect(page.getByTestId('toast').last()).toContainText('Contrato salvo.');
    await expect(linha).toContainText('VIGENTE');
    await expect(linha).toContainText('1 produto(s)');

    // a ficha do contrato: abre pelo nome do fornecedor na tela de Contratos
    await page.goto('/contratos');
    await page.getByRole('link', { name: `E2E Contrato ${marca} LTDA` }).click();
    const ficha = page.getByTestId('ficha-contrato');
    await expect(ficha).toContainText(`CT-E2E-${marca}`);
    await expect(ficha).toContainText('VIGENTE');
    const historia = page.getByTestId('linha-do-tempo-contrato');
    await expect(historia.locator('[data-tipo="CRIADO"]')).toContainText(`Contrato CT-E2E-${marca} cadastrado`);

    // o contrato assinado entra nos documentos e na história
    await page.getByRole('button', { name: 'Anexar documento do contrato' }).click();
    await page.getByLabel('Arquivo').setInputFiles({
      name: `contrato-${marca}.pdf`, mimeType: 'application/pdf', buffer: Buffer.from('%PDF-1.4\n%E2E\n'),
    });
    await page.getByRole('button', { name: 'Anexar', exact: true }).click();
    await expect(page.getByTestId('toast').last()).toContainText('Documento anexado ao contrato.');
    await expect(page.getByTestId('documentos-do-contrato').locator('[data-documento="CONTRATO"]'))
      .toContainText(`contrato-${marca}.pdf`);
    await expect(historia.locator('[data-tipo="DOCUMENTO_ANEXADO"]')).toContainText(`contrato-${marca}.pdf`);

    // de volta ao cadastro, pela busca: a lista é paginada e o fornecedor pode não estar na primeira página
    await page.goto('/fornecedores');
    await page.getByLabel('Buscar', { exact: true }).fill(cnpj);

    // remover o produto e salvar encerra o contrato
    await linha.getByRole('button', { name: /Mais ações de/ }).click();
    await page.getByRole('menuitem', { name: 'Contrato de parceria' }).click();
    await page.locator('#contrato').getByRole('button', { name: 'Remover' }).first().click();
    await page.locator('#contrato').getByRole('button', { name: 'Salvar contrato' }).click();
    await expect(page.getByTestId('toast').last()).toContainText('Contrato encerrado');
    await expect(linha).toContainText('sem contrato');
  });

  test('consultar a sessão muitas vezes não derruba o usuário', async ({ page }) => {
    // /auth/me é chamado a cada carga de tela; o limite por IP vale para o login,
    // não para a consulta da sessão (senão navegar entre telas deslogava)
    await abrirAutenticado(page, '/pedidos');
    const status = await page.evaluate(async () => {
      const saida: number[] = [];
      for (let i = 0; i < 15; i++) {
        const res = await fetch('/api/v1/auth/me', {
          headers: { Authorization: 'Bearer ' + sessionStorage.getItem('ts.access') },
        });
        saida.push(res.status);
      }
      return saida;
    });
    expect(status.filter((s) => s !== 200)).toEqual([]);
    await page.reload();
    await expect(page.getByTestId('tabela-pedidos')).toBeVisible();
  });

  test('a raiz abre o painel e o menu navega sem recarregar a página', async ({ page }) => {
    await abrirAutenticado(page, '/');
    await expect(page).toHaveURL(/\/painel$/);

    const menu = page.locator('aside[aria-label="Menu"]');
    await menu.getByRole('button', { name: 'Cadastros' }).click();
    await menu.getByRole('link', { name: 'Fornecedores', exact: true }).click();
    await expect(page).toHaveURL(/\/fornecedores$/);
    await expect(page.locator('#titulo-pagina')).toHaveText('Fornecedores');

    // navegação do roteador: o app não recarrega entre telas
    await menu.getByRole('link', { name: 'Usuários' }).click();
    await expect(page.locator('#titulo-pagina')).toHaveText('Usuários');
  });

  test('as URLs antigas em /app/ continuam levando à tela certa', async ({ page }) => {
    await abrirAutenticado(page, '/app/fornecedores');
    await expect(page).toHaveURL(/\/fornecedores$/);
    await expect(page.locator('#titulo-pagina')).toHaveText('Fornecedores');

    await page.goto('/app/pedidos');
    await expect(page).toHaveURL(/\/pedidos$/);
  });
});
