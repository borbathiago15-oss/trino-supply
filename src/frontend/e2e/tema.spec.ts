import { expect, test } from '@playwright/test';
import { abrirAutenticado } from './sessao';

/**
 * Claro ou escuro é escolha de cada pessoa e sobrevive à recarga: a classe `dark` no <html>
 * é o que o CSS lê, e o navegador guarda a escolha. No escuro a tela continua legível e
 * inteira — sem rolar de lado no celular.
 */
test('o tema escuro liga pelo cabeçalho, fica depois de recarregar e desliga de novo', async ({ page }) => {
  await abrirAutenticado(page, '/painel');
  const html = page.locator('html');
  await expect(html).not.toHaveClass(/dark/);

  await page.getByRole('button', { name: 'Tema escuro' }).click();
  await expect(html).toHaveClass(/dark/);
  await page.reload();
  await expect(page.getByTestId('kpis-painel')).toBeVisible();
  await expect(html).toHaveClass(/dark/);

  await page.getByRole('button', { name: 'Tema claro' }).click();
  await expect(html).not.toHaveClass(/dark/);
});

test.describe('celular (375px)', () => {
  test.use({ viewport: { width: 375, height: 812 } });
  test('no escuro, o app de bolso cabe sem rolar de lado', async ({ page }) => {
    await page.addInitScript(() => localStorage.setItem('ts.tema', 'escuro'));
    await abrirAutenticado(page, '/app');
    await expect(page.locator('html')).toHaveClass(/dark/);
    await expect(page.getByTestId('abas-do-app')).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(375);
    // a escolha não vaza para os outros testes
    await page.evaluate(() => localStorage.removeItem('ts.tema'));
  });
});
