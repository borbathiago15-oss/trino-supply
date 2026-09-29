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

    // o splash é o ícone sobre o background_color: os dois no preto da marca, como o login,
    // para o logo aparecer inteiro num fundo só — e cada ícone declarado existe, é PNG e tem
    // o tamanho que diz (ícone que não carrega vira o "C" cortado da tela de abertura)
    expect(m.background_color).toBe('#0b0d0f');
    expect(m.theme_color).toBe('#0b0d0f');
    expect(m.icons.map((i: { purpose: string }) => i.purpose).sort()).toEqual(['any', 'any', 'maskable']);
    for (const icone of m.icons as { src: string; sizes: string; type: string }[]) {
      const r = await request.get(icone.src);
      expect(r.status(), icone.src).toBe(200);
      expect(r.headers()['content-type'], icone.src).toContain('image/png');
      const png = await r.body();
      // largura e altura ficam no cabeçalho IHDR, nos bytes 16–23
      const largura = png.readUInt32BE(16);
      const altura = png.readUInt32BE(20);
      expect(`${largura}x${altura}`, icone.src).toBe(icone.sizes);
    }
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

    // o menu do sistema não aparece no app, e não há aba que leve a ele
    await expect(page.getByRole('button', { name: 'Abrir o menu' })).toHaveCount(0);
    await expect(abas.getByRole('link', { name: 'Sistema' })).toHaveCount(0);
    // o link da diretoria para a Central fica dentro do app
    await abas.getByRole('link', { name: 'Diretoria' }).click();
    await page.getByRole('link', { name: 'Central de Aprovação →' }).click();
    await expect(page).toHaveURL(/\/app\/aprovacoes$/);
    await expect(abas).toBeVisible();
  });
});
