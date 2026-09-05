import { expect, test } from '@playwright/test';
import { abrirAutenticado } from './sessao';

const marca = Date.now().toString().slice(-6);

test.describe('Usuários e Empresas (React)', () => {
  test('usuário: cria com autorizações, edita e inativa', async ({ page }) => {
    await abrirAutenticado(page, '/usuarios');
    const email = `e2e.${marca}@trinosupply.com.br`;

    await page.fill('#usu-nome', `E2E Usuário ${marca}`);
    await page.fill('#usu-email', email);
    await page.selectOption('#usu-papel', 'Requester');
    // escolher o papel já sugere as autorizações dele
    await expect(page.getByLabel('Solicitações de Compra')).toBeChecked();
    await page.fill('#usu-senha', 'SenhaSegura2026!');
    await page.getByRole('button', { name: 'Criar usuário' }).click();
    await expect(page.getByTestId('toast')).toContainText('Usuário criado');

    const linha = page.locator(`tr[data-usuario="${email}"]`);
    await expect(linha).toContainText('Solicitante');
    await expect(linha).toContainText('Solicitações de Compra');
    await expect(linha).toContainText('ATIVO');

    // editar: e-mail e senha ficam travados, cada um tem caminho próprio
    await linha.getByRole('button', { name: 'Editar' }).click();
    await expect(page.locator('#usu-email')).toBeDisabled();
    await expect(page.locator('#usu-senha')).toBeDisabled();
    await page.fill('#usu-nome', `E2E Usuário ${marca} revisado`);
    await page.getByRole('button', { name: 'Salvar alterações' }).click();
    await expect(page.getByTestId('toast').last()).toContainText('Usuário atualizado');
    await expect(linha).toContainText(`E2E Usuário ${marca} revisado`);

    // inativar pede confirmação e avisa que as sessões caem
    await linha.getByRole('button', { name: /Mais ações de/ }).click();
    await page.getByRole('menuitem', { name: 'Inativar' }).click();
    await expect(page.getByRole('dialog')).toContainText('sessões dele serão encerradas');
    await page.getByRole('dialog').getByRole('button', { name: 'Inativar' }).click();
    await expect(linha).toContainText('INATIVO');

    await linha.getByRole('button', { name: /Mais ações de/ }).click();
    await page.getByRole('menuitem', { name: 'Reativar' }).click();
    await expect(linha).toContainText('ATIVO');
  });

  test('usuário: nova senha exige o mínimo de caracteres', async ({ page }) => {
    await abrirAutenticado(page, '/usuarios');
    const linha = page.locator('tr[data-usuario]').first();
    await linha.getByRole('button', { name: /Mais ações de/ }).click();
    await page.getByRole('menuitem', { name: 'Nova senha' }).click();

    await page.fill('#usu-nova-senha', 'curta');
    await page.getByRole('button', { name: 'Redefinir senha' }).click();
    await expect(page.getByTestId('toast').last()).toContainText('pelo menos 12 caracteres');
    await expect(page.getByRole('dialog')).toBeVisible();
    await page.getByRole('button', { name: 'Cancelar' }).click();
    await expect(page.getByRole('dialog')).toHaveCount(0);
  });

  test('empresa: cadastra CNPJ, edita com o documento travado e salva o padrão da O.C.', async ({ page }) => {
    await abrirAutenticado(page, '/empresas');
    // CNPJ de teste com 14 dígitos, único por execução
    const cnpj = `${marca}00000199`;

    const formulario = page.locator('#form-empresa');
    await formulario.locator('#emp-razao').fill(`E2E Empresa ${marca} LTDA`);
    await formulario.locator('#emp-cnpj').fill(cnpj);
    await formulario.locator('#emp-endereco').fill('Av. dos Testes, 100');
    await formulario.locator('#emp-cidade').fill('Vitória');
    await formulario.locator('#emp-uf').fill('ES');
    await formulario.locator('#emp-cep').fill('29000-000');
    await page.getByRole('button', { name: 'Cadastrar CNPJ' }).click();
    await expect(page.getByTestId('toast')).toContainText('CNPJ cadastrado');

    const linha = page.locator(`tr[data-empresa="${cnpj}"]`);
    await expect(linha).toContainText('Vitória/ES');
    await expect(linha).toContainText('ATIVA');

    await linha.getByRole('button', { name: 'Editar' }).click();
    await expect(formulario.locator('#emp-cnpj')).toBeDisabled();
    await formulario.locator('#emp-cidade').fill('Serra');
    await page.getByRole('button', { name: 'Salvar alterações' }).click();
    await expect(linha).toContainText('Serra/ES');

    await linha.getByRole('button', { name: 'Inativar' }).click();
    await expect(linha).toContainText('INATIVA');

    // padrão da O.C.: preenchido do zero (banco novo não tem nenhum) e relido
    const padrao = page.locator('#padrao-oc');
    const clausula = `Cláusula conferida pelo E2E ${marca}`;
    await padrao.locator('#oc-razao').fill('Trino Supply Serviços LTDA');
    await padrao.locator('#oc-cnpj').fill('11222333000144');
    await padrao.locator('#oc-endereco').fill('Av. Central, 1000');
    await padrao.locator('#oc-cidade').fill('Vitória');
    await padrao.locator('#oc-uf').fill('ES');
    await padrao.locator('#oc-cep').fill('29000-000');
    await padrao.locator('#oc-clausulas').fill(clausula);
    await page.getByRole('button', { name: 'Salvar dados da empresa' }).click();
    await expect(page.getByTestId('toast').last()).toContainText('Dados da empresa salvos');

    await page.reload();
    await expect(page.locator('#oc-clausulas')).toHaveValue(clausula);
    await expect(page.locator('#oc-razao')).toHaveValue('Trino Supply Serviços LTDA');
  });
});
