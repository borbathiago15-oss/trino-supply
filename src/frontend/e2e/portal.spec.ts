import { expect, test } from '@playwright/test';

/**
 * O portal é do fornecedor: sessão própria (`portal.token`), sem a sessão do
 * time interno. Por isso estes testes não usam `abrirAutenticado`.
 */
test.describe('Portal do Fornecedor (React)', () => {
  test('a URL antiga /portal leva à tela nova', async ({ page }) => {
    await page.goto('/portal');
    await expect(page).toHaveURL(/\/app\/portal$/);
    await expect(page.getByLabel('CNPJ / CPF')).toBeVisible();
    await expect(page.locator('body')).toContainText('Portal do Fornecedor');
  });

  test('sem sessão do fornecedor, o portal abre no login e não mostra o menu interno', async ({ page }) => {
    await page.goto('/app/portal');
    await expect(page.getByLabel('Chave de acesso')).toBeVisible();
    // nada do app interno vaza para cá
    await expect(page.locator('aside[aria-label="Menu"]')).toHaveCount(0);
    await expect(page.locator('#titulo-pagina')).toHaveCount(0);
  });

  test('credencial inválida avisa e mantém o fornecedor no login', async ({ page }) => {
    await page.goto('/app/portal');
    await page.getByLabel('CNPJ / CPF').fill('00000000000000');
    await page.getByLabel('Chave de acesso').fill('chave-que-nao-existe');
    await page.getByRole('button', { name: 'Entrar no portal' }).click();

    await expect(page.getByTestId('toast')).toContainText(/inválidos|sem acesso/);
    await expect(page.getByLabel('CNPJ / CPF')).toBeVisible();
  });

  test('a sessão do time interno não dá acesso ao portal', async ({ page }) => {
    // injeta a sessão interna e confirma que o portal segue pedindo a chave do fornecedor
    await page.addInitScript(() => sessionStorage.setItem('ts.access', 'token-interno-qualquer'));
    await page.goto('/app/portal');
    await expect(page.getByLabel('CNPJ / CPF')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Sair' })).toHaveCount(0);
  });
});
