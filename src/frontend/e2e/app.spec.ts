import { expect, test } from '@playwright/test';
import { abrirAutenticado } from './sessao';

/**
 * O app de bolso: o mesmo sistema, instalável pelo manifesto, com três abas no celular.
 * O que se protege aqui é o que faz dele um app — o manifesto servido, a abertura em /app e as
 * abas que trocam de tela sem a página rolar de lado.
 */
test.describe('App de bolso (375px)', () => {
  test.use({ viewport: { width: 375, height: 812 } });

  test('o manifesto é servido e abre o app em /app', async ({ request }) => {
    const r = await request.get('/manifest.webmanifest');
    expect(r.status()).toBe(200);
    expect(r.headers()['content-type']).toContain('manifest+json');
    const m = await r.json();
    expect(m.start_url).toBe('/app');
    expect(m.display).toBe('standalone');
  });

  test('abre na Central, e as abas levam à diretoria e ao dashboard', async ({ page }) => {
    await abrirAutenticado(page, '/app');
    await expect(page).toHaveURL(/\/app\/aprovacoes$/);
    const abas = page.getByTestId('abas-do-app');
    await expect(abas).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(375);

    await abas.getByRole('link', { name: 'Diretoria' }).click();
    await expect(page).toHaveURL(/\/app\/diretoria$/);
    await expect(page.getByTestId('numero-gasto')).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(375);

    await abas.getByRole('link', { name: 'Dashboard' }).click();
    await expect(page).toHaveURL(/\/app\/painel$/);
    await expect(page.getByTestId('kpis-painel')).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(375);

    // o menu do sistema não aparece no app
    await expect(page.getByRole('button', { name: 'Abrir o menu' })).toHaveCount(0);
  });
});
