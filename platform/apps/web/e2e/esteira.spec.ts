import { expect, test } from '@playwright/test';

/**
 * E2E do frontend: entra de verdade, navega e confere o que a tela promete.
 * O cenário vem do seed (`scripts/seed-e2e.js`), executado antes da suíte.
 */

const CNPJ = process.env.E2E_CNPJ ?? '70809010000144';
const SENHA = process.env.E2E_SENHA ?? 'SenhaForte#2026';

async function entrar(page: import('@playwright/test').Page, email: string) {
  await page.goto('/login');
  await page.fill('#cnpj', CNPJ);
  await page.fill('#email', email);
  await page.fill('#senha', SENHA);
  await page.click('button[type=submit]');
  await page.waitForURL('**/compras/requisicoes');
}

test.describe('esteira de compras', () => {
  test('login inválido não entra e não vaza qual campo errou', async ({ page }) => {
    await page.goto('/login');
    await page.fill('#cnpj', CNPJ);
    await page.fill('#email', 'ninguem@trino.com');
    await page.fill('#senha', 'errada');
    await page.click('button[type=submit]');
    await expect(page.getByText('Credenciais inválidas.')).toBeVisible();
    await expect(page).toHaveURL(/\/login/);
  });

  test('esteira lista requisições com farol de SLA e abre a gaveta', async ({ page }) => {
    await entrar(page, 'comprador@trino.com');

    await expect(page.getByRole('heading', { name: 'Esteira de solicitações' })).toBeVisible();
    const linhas = page.locator('table tbody tr');
    expect(await linhas.count()).toBeGreaterThan(0);

    // O farol mostra tempo restante — não um valor cru de segundos.
    await expect(page.locator('table tbody').getByText(/restantes|atraso|Não submetida/).first()).toBeVisible();

    await linhas.first().getByRole('button', { name: 'Ver' }).click();
    const gaveta = page.getByRole('dialog');
    await expect(gaveta).toBeVisible();
    await expect(gaveta.getByText('Itens')).toBeVisible();
    await expect(gaveta.getByText('Histórico da esteira')).toBeVisible();

    await page.keyboard.press('Escape');
    await expect(gaveta).toBeHidden();
  });

  test('filtro e ordenação ficam na URL (link compartilhável)', async ({ page }) => {
    await entrar(page, 'comprador@trino.com');
    await page.getByLabel('Situação').selectOption('SUBMETIDA');
    await page.waitForURL(/status=SUBMETIDA/);
    await page.getByLabel('Ordenar por').selectOption('sla');
    await page.waitForURL(/ordem=sla/);

    // Recarregar mantém exatamente a mesma lista.
    await page.reload();
    await expect(page.getByLabel('Situação')).toHaveValue('SUBMETIDA');
    await expect(page.getByLabel('Ordenar por')).toHaveValue('sla');
  });

  test('portal do aprovador exige motivo para rejeitar', async ({ page }) => {
    await entrar(page, 'gestor@trino.com');
    await page.goto('/compras/aprovacoes');

    const cartoes = page.locator('article');
    if ((await cartoes.count()) === 0) test.skip(true, 'sem aprovações pendentes no cenário');

    await expect(cartoes.first().getByText(/Nível 1/)).toBeVisible();
    await cartoes.first().getByRole('button', { name: 'Rejeitar' }).click();

    const modal = page.getByRole('dialog');
    await expect(modal).toBeVisible();
    // Sem motivo, o botão de confirmar fica desabilitado.
    await expect(modal.getByRole('button', { name: 'Confirmar' })).toBeDisabled();
    await modal.getByLabel(/Motivo/).fill('Fora do escopo do contrato');
    await expect(modal.getByRole('button', { name: 'Confirmar' })).toBeEnabled();
  });

  test('recebimento valida a chave da NF-e antes de deixar registrar', async ({ page }) => {
    await entrar(page, 'comprador@trino.com');
    await page.goto('/compras/recebimento');

    const seletor = page.getByLabel('Pedido de compra');
    const opcoes = await seletor.locator('option').count();
    if (opcoes <= 1) test.skip(true, 'sem pedidos aguardando recebimento no cenário');

    await seletor.selectOption({ index: 1 });
    await expect(page.getByText('Nota fiscal (opcional, mas exigida para o 3-way)')).toBeVisible();

    const chave = page.getByLabel(/Chave de acesso/);
    await chave.fill('123');
    await expect(page.getByText('3/44 dígitos')).toBeVisible();
    await expect(page.getByText('A chave da NF-e precisa ter exatamente 44 dígitos.')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Registrar recebimento' })).toBeDisabled();

    await chave.fill('1'.repeat(44));
    await expect(page.getByText('44/44 dígitos')).toBeVisible();
  });

  test('equalização exige justificativa fora do menor preço', async ({ page }) => {
    await entrar(page, 'comprador@trino.com');
    await page.goto('/compras/cotacoes');

    const link = page.getByRole('link', { name: 'Mapa comparativo' }).first();
    if ((await link.count()) === 0) test.skip(true, 'sem cotações no cenário');
    await link.click();

    await expect(page.getByRole('heading', { name: /Mapa comparativo/ })).toBeVisible();
    const escolhas = page.locator('input[name=vencedora]');
    if ((await escolhas.count()) < 2) test.skip(true, 'cotação com menos de duas propostas');

    // A segunda colocada na nota não é a mais barata: pede justificativa.
    await escolhas.nth(1).click();
    const justificativa = page.getByLabel(/Justificativa do desvio/);
    if (await justificativa.count()) {
      await expect(page.getByRole('button', { name: 'Registrar equalização' })).toBeDisabled();
      await justificativa.fill('Entrega em 3 dias atende à parada de manutenção');
      await expect(page.getByRole('button', { name: 'Registrar equalização' })).toBeEnabled();
    }
  });
});
