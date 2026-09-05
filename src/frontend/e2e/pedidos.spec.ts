import { expect, test } from '@playwright/test';
import { SENHA_E2E } from './cenario';
import { abrirAutenticado, lerCenario } from './sessao';

const EMAIL = process.env.ADMIN_EMAIL ?? 'admin@trinosupply.com.br';
// depois do preparo do cenário o admin já trocou a senha provisória (SEC-004)
const SENHA = SENHA_E2E;

// a OC do SENIOR é única no sistema: um número fixo faria a segunda execução
// contra o mesmo banco esbarrar na própria regra
const ocDoErp = `OC-ERP-${Date.now().toString().slice(-6)}`;

test.describe('Pedidos de Compra (React)', () => {
  test('lista os pedidos, abre o detalhe e percorre OC → NF → entrega', async ({ page }) => {
    await abrirAutenticado(page, '/pedidos');

    // o pedido desta execução, criado pelo seed (o banco pode ter outros)
    const { pedidoNumero: numero } = lerCenario();
    const tabela = page.getByTestId('tabela-pedidos');
    await expect(tabela).toBeVisible();
    const linha = tabela.locator(`tr[data-pedido="${numero}"]`);
    await expect(linha).toContainText('OC/Faturamento');
    await expect(linha).toContainText('a registrar');

    // filtro em memória
    await page.getByLabel('Buscar').fill('não existe esse pedido');
    await expect(page.getByText('Nenhum pedido corresponde ao filtro.')).toBeVisible();
    await page.getByLabel('Buscar').fill(numero);
    await expect(tabela.locator('tr[data-pedido]')).toHaveCount(1);

    // detalhe
    await linha.getByRole('button', { name: 'Abrir' }).click();
    await expect(page).toHaveURL(/\/pedidos\/[0-9a-f-]{36}$/);
    const detalhe = page.getByTestId('pedido-detalhe');
    await expect(detalhe).toContainText(`Pedido ${numero}`);
    await expect(detalhe).toContainText('Luva nitrílica tamanho M');

    // OC do ERP
    await page.fill('#oc-numero', ocDoErp);
    await page.fill('#oc-data', '2026-09-01');
    await page.getByRole('button', { name: 'Registrar OC' }).click();
    await expect(page.getByTestId('toast')).toContainText('OC registrada.');
    await expect(page.locator('#oc-erp')).toContainText(`OC ${ocDoErp} de 01/09/2026`);

    // nota fiscal
    await page.fill('#nf-numero', '000123');
    await page.fill('#nf-data', '2026-09-02');
    await page.fill('#nf-valor', '197');
    await page.getByRole('button', { name: 'Lançar NF' }).click();
    await expect(page.getByTestId('tabela-notas')).toContainText('000123');
    await expect(detalhe).toHaveAttribute('data-situacao', 'FATURADO');

    // entrega parcial: só a luva chega
    await page.getByLabel('Chegou agora: Luva nitrílica tamanho M').fill('10');
    await page.getByRole('button', { name: 'Registrar entrega' }).click();
    await expect(page.getByTestId('toast').last()).toContainText('Entrega registrada.');
    await expect(detalhe).toHaveAttribute('data-situacao', 'PARCIAL');
    // a ordem dos itens não é garantida: procura a linha do item que chegou
    const linhaLuva = page.getByTestId('tabela-entrega').locator('tr[data-item]', { hasText: 'Luva nitrílica' });
    await expect(linhaLuva).toContainText('10 PAR');

    // encerra o saldo que não vai chegar: parte chegou, então o pedido fica
    // "Entregue parcial" (regra do PurchaseOrderService) e não recebe mais nada
    await page.fill('#entrega-encerrar', 'Fornecedor não tem mais o óculos em estoque');
    await page.getByRole('button', { name: 'Encerrar saldo' }).click();
    await expect(page.getByTestId('toast').last()).toContainText('Saldo encerrado.');
    await expect(detalhe).toHaveAttribute('data-situacao', 'PARCIAL');
    await expect(page.locator('#entrega')).toContainText('Entrega concluída em');
    await expect(page.getByRole('button', { name: 'Registrar entrega' })).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Registrar OC' })).toHaveCount(0);

    // de volta à lista, a situação e o motivo acompanham
    await page.getByRole('link', { name: '← Pedidos de compra' }).click();
    const linhaFinal = page.getByTestId('tabela-pedidos').locator(`tr[data-pedido="${numero}"]`);
    await expect(linhaFinal).toContainText('Entregue parcial');
    await expect(linhaFinal).toContainText('Fornecedor não tem mais o óculos em estoque');
    await expect(linhaFinal).toContainText('recebido 10 de 14');
  });

  test('sem sessão, uma rota protegida cai no login e volta ao destino depois', async ({ page }) => {
    await page.goto('/pedidos');
    await expect(page).toHaveURL(/\/login$/);
    await page.fill('#email', EMAIL);
    await page.fill('#password', SENHA);
    await page.click('button[type=submit]');
    await expect(page).toHaveURL(/\/pedidos$/);
    await expect(page.locator('#titulo-pagina')).toHaveText('Pedidos de Compra');
  });

  test('o login pela tela vale nas outras rotas da mesma aba', async ({ page }) => {
    await page.goto('/login');
    await page.locator('#email').fill(EMAIL);
    await page.locator('#password').fill(SENHA);
    await page.getByRole('button', { name: /Entrar/ }).click();
    await expect(page.locator('#titulo-pagina')).toBeVisible();

    // a sessão fica na aba: outra rota abre sem novo login
    await page.goto('/pedidos');
    await expect(page.locator('#titulo-pagina')).toHaveText('Pedidos de Compra');
    await expect(page.getByTestId('tabela-pedidos')).toBeVisible();
  });
});
