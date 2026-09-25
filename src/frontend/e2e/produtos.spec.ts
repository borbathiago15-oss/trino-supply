import { expect, test } from '@playwright/test';
import { abrirAutenticado } from './sessao';

const marca = Date.now().toString().slice(-6);

test.describe('Cadastro de Produtos (React)', () => {
  test('cadastra, busca, edita e inativa um produto', async ({ page }) => {
    await abrirAutenticado(page, '/produtos');
    const descricao = `E2E Produto ${marca}`;

    // o resumo do catálogo aparece antes de qualquer busca
    await expect(page.locator('#view-root, body')).toContainText('produto(s) ativo(s)');

    // busca sem critério não dispara consulta
    await page.getByRole('button', { name: 'Buscar' }).click();
    await expect(page.getByTestId('toast')).toContainText('Digite um código ou parte da descrição');

    await page.getByRole('button', { name: '+ Novo produto' }).click();
    await page.fill('#prod-codigo', `E2E-${marca}`);
    await page.selectOption('#prod-form-familia', { index: 1 });
    await page.fill('#prod-descricao', descricao);
    await page.fill('#prod-unidade', 'UN');
    await page.fill('#prod-preco', '19.90');
    await page.getByRole('button', { name: 'Adicionar produto' }).click();
    await expect(page.getByTestId('toast').last()).toContainText('adicionado ao catálogo');

    // busca pelo código recém-criado
    await page.getByLabel('Buscar produto').fill(`E2E-${marca}`);
    await page.getByRole('button', { name: 'Buscar' }).click();
    const linha = page.locator(`tr[data-produto="E2E-${marca}"]`);
    await expect(linha).toContainText(descricao);
    await expect(linha).toContainText('R$ 19,90');
    await expect(linha).toContainText('ATIVO');

    // editar: o código é a identidade e fica travado
    await linha.getByRole('button', { name: 'Editar' }).click();
    await expect(page.locator('#prod-codigo')).toBeDisabled();
    await page.fill('#prod-descricao', `${descricao} revisado`);
    await page.getByRole('button', { name: 'Salvar alterações' }).click();
    await expect(page.getByTestId('toast').last()).toContainText('Produto atualizado.');
    await expect(linha).toContainText(`${descricao} revisado`);

    await linha.getByRole('button', { name: 'Inativar' }).click();
    await expect(linha).toContainText('INATIVO');
    await linha.getByRole('button', { name: 'Reativar' }).click();
    await expect(linha).toContainText('ATIVO');
  });

  test('EPI sem C.A. é barrado; com C.A. de um fornecedor entra', async ({ page }) => {
    await abrirAutenticado(page, '/produtos');
    const codigo = `E2E-EPI-${marca}`;

    await page.getByRole('button', { name: '+ Novo produto' }).click();
    await page.fill('#prod-codigo', codigo);
    await page.selectOption('#prod-form-familia', { index: 1 });
    await page.fill('#prod-descricao', `E2E Bota ${marca}`);

    // escolher um tipo que exige C.A. mostra o aviso e cria a primeira linha de fornecedor
    const tipoComCa = await page.locator('#prod-tipo option').evaluateAll((os) =>
      os.map((o) => (o as HTMLOptionElement).value).filter(Boolean));
    test.skip(!tipoComCa.includes('EPI'), 'ambiente sem o tipo EPI');
    await page.selectOption('#prod-tipo', 'EPI');
    await expect(page.getByTestId('aviso-ca')).toBeVisible();

    await page.getByLabel('Fornecedor do produto').first().selectOption({ index: 0 });
    await page.getByRole('button', { name: 'Adicionar produto' }).click();
    await expect(page.getByTestId('toast').last()).toContainText('EPI e EPC exigem o C.A.');

    await page.getByLabel('C.A. do fornecedor').first().fill('98765');
    await page.getByRole('button', { name: 'Adicionar produto' }).click();
    await expect(page.getByTestId('toast').last()).toContainText('adicionado ao catálogo');

    await page.getByLabel('Buscar produto').fill(codigo);
    await page.getByRole('button', { name: 'Buscar' }).click();
    const linha = page.locator(`tr[data-produto="${codigo}"]`);
    await expect(linha).toContainText('C.A. 98765');
    await expect(linha).not.toContainText('sem C.A. em nenhum fornecedor');
  });

  test('importação por planilha pré-visualiza antes de gravar', async ({ page }) => {
    await abrirAutenticado(page, '/produtos');
    await page.getByRole('button', { name: 'Importar planilha' }).click();
    const painel = page.locator('#importacao');
    await expect(painel).toBeVisible();

    const csv = `Produto;Descrição\nIMP-${marca}-1;E2E Importado um\nIMP-${marca}-2;E2E Importado dois\n`;
    await painel.getByLabel('Planilha').setInputFiles({ name: 'produtos.csv', mimeType: 'text/csv', buffer: Buffer.from(csv, 'utf8') });
    await painel.getByLabel('Família').selectOption({ index: 1 });
    await painel.getByRole('button', { name: 'Pré-visualizar' }).click();

    const resultado = page.getByTestId('resultado-importacao');
    await expect(resultado).toContainText('2 linha(s) lida(s)');
    await expect(resultado).toContainText('2 produto(s) a criar');
    // a prévia não grava nada: o produto ainda não existe
    await page.getByRole('button', { name: 'Fechar' }).click();
    await page.getByLabel('Buscar produto').fill(`IMP-${marca}-1`);
    await page.getByRole('button', { name: 'Buscar' }).click();
    await expect(page.getByText('Nenhum produto encontrado com esse critério.')).toBeVisible();

    // confirmar grava e os produtos passam a ser encontrados
    await page.getByRole('button', { name: 'Importar planilha' }).click();
    await painel.getByLabel('Planilha').setInputFiles({ name: 'produtos.csv', mimeType: 'text/csv', buffer: Buffer.from(csv, 'utf8') });
    await painel.getByLabel('Família').selectOption({ index: 1 });
    await painel.getByRole('button', { name: 'Pré-visualizar' }).click();
    await painel.getByRole('button', { name: 'Confirmar importação' }).click();
    await expect(page.getByTestId('toast').last()).toContainText('produto(s) importado(s)');

    await page.getByLabel('Buscar produto').fill(`IMP-${marca}-`);
    await page.getByRole('button', { name: 'Buscar' }).click();
    await expect(page.getByTestId('tabela-produtos').locator('tr[data-produto]')).toHaveCount(2);
  });

  test('produto novo aparece na ficha da SC e, sem uso, se exclui de vez', async ({ page }) => {
    const codigo = `E2E-DEL-${marca}`;
    const descricao = `E2E Excluível ${marca}`;
    await abrirAutenticado(page, '/produtos');
    await page.getByRole('button', { name: '+ Novo produto' }).click();
    await page.fill('#prod-codigo', codigo);
    await page.selectOption('#prod-form-familia', { index: 1 });
    await page.fill('#prod-descricao', descricao);
    await page.fill('#prod-unidade', 'UN');
    await page.getByRole('button', { name: 'Adicionar produto' }).click();
    await expect(page.getByTestId('toast').last()).toContainText('adicionado ao catálogo');

    // na SC, a busca é a única porta, e o clique abre a ficha antes de usar
    await abrirAutenticado(page, '/solicitacoes/nova');
    await page.getByRole('button', { name: 'Buscar no catálogo' }).first().click();
    const busca = page.getByRole('dialog', { name: 'Escolher produto do catálogo' });
    await busca.getByLabel(/Buscar produto/).fill(codigo);
    await busca.getByRole('button', { name: new RegExp(descricao) }).click();
    const ficha = page.getByTestId('ficha-do-produto');
    await expect(ficha).toContainText(codigo);
    await expect(ficha).toContainText(descricao);
    await page.getByRole('button', { name: 'Usar este produto' }).click();
    await expect(page.locator('[data-linha-item]').first()).toContainText(descricao);

    // a SC não foi gravada: o produto nunca circulou, e sai de vez
    await abrirAutenticado(page, '/produtos');
    await page.getByLabel('Buscar produto').fill(codigo);
    await page.getByRole('button', { name: 'Buscar' }).click();
    const linha = page.locator(`tr[data-produto="${codigo}"]`);
    await linha.getByRole('button', { name: 'Excluir' }).click();
    await page.getByRole('dialog', { name: 'Excluir produto' }).getByRole('button', { name: 'Excluir' }).click();
    await expect(page.getByTestId('toast').last()).toContainText(`Produto ${codigo} excluído.`);
    await expect(linha).toHaveCount(0);
  });
});
