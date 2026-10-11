import { expect, test } from '@playwright/test';
import { SENHA_E2E } from './cenario';
import { segredoDoMfa } from './sessao';
import { totp } from './totp';

const EMAIL = process.env.ADMIN_EMAIL ?? 'admin@trinosupply.com.br';

/**
 * A segunda etapa do login pela tela: o administrador tem o autenticador cadastrado (o
 * cenário o cadastra na primeira execução), então a senha certa pede o código, e só o código
 * certo abre o sistema. Um login só, por causa do limite por IP.
 */
test.describe('Verificação em duas etapas', () => {
  test('a senha certa pede o código do autenticador, e o código abre o sistema', async ({ page }) => {
    const segredo = segredoDoMfa();
    expect(segredo, 'o cenário guarda o segredo do autenticador do administrador em .e2e/mfa.json').not.toBeNull();

    await page.goto('/login');
    await page.locator('#email').fill(EMAIL);
    await page.locator('#password').fill(SENHA_E2E);
    await page.getByRole('button', { name: /Entrar/ }).click();

    const etapa = page.getByTestId('segunda-etapa');
    await expect(etapa).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Confirme que é você' })).toBeVisible();
    // a sessão não existe até o código: a casca do sistema não carregou
    await expect(page.getByRole('complementary', { name: 'Menu' })).toBeHidden();

    await page.locator('#codigo').fill('000000');
    await page.getByRole('button', { name: 'Confirmar' }).click();
    await expect(page.getByRole('alert')).toContainText('Código inválido');

    await page.locator('#codigo').fill(totp(segredo!));
    await page.getByRole('button', { name: 'Confirmar' }).click();
    // o destino de quem entra sem rota guardada; o que importa é que a casca carregou
    await expect(page).toHaveURL(/\/pedidos$/);
    await expect(page.getByRole('complementary', { name: 'Menu' })).toBeVisible();
  });
});
