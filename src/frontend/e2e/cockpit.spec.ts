import { expect, test } from '@playwright/test';
import { abrirAutenticado, tokenDaSessao } from './sessao';

/** Se a página rola em algum eixo — o critério de aceite 1 da parede. */
const rolagem = (page: import('@playwright/test').Page) => page.evaluate(() => ({
  rolaX: document.documentElement.scrollWidth > document.documentElement.clientWidth,
  rolaY: document.documentElement.scrollHeight > document.documentElement.clientHeight,
}));

/**
 * War Room Cockpit — a TV da sala de suprimentos.
 *
 * O que só o E2E prova é o critério de aceite 1: a tela cabe inteira em Full HD, sem
 * barra de rolagem. Isso não dá para verificar em teste de unidade, e é justamente o
 * que quebra numa parede — conteúdo cortado que ninguém pode arrastar de volta.
 */
test.describe('Cockpit (Modo TV)', () => {
  test.use({ viewport: { width: 1920, height: 1080 } });

  test('cabe inteiro em Full HD, sem rolagem em nenhum eixo', async ({ page }) => {
    await abrirAutenticado(page, '/cockpit');
    await expect(page.getByTestId('cockpit')).toBeVisible();

    const { rolaX, rolaY } = await rolagem(page);
    expect(rolaX).toBe(false);
    expect(rolaY).toBe(false);
  });

  /**
   * O bloco do almoxarifado só entra na parede quando há o que dizer, então o teste semeia
   * a solicitação de material que ele conta: depender de `material.spec.ts` ter rodado antes
   * faria o resultado mudar com a ordem da suíte. E a faixa nova disputa altura com a esteira
   * e o radar, então aqui se confere de novo que nada passou a rolar.
   */
  test('o bloco do almoxarifado aparece e a tela continua sem rolagem', async ({ page, request }) => {
    const headers = { Authorization: `Bearer ${tokenDaSessao()}`, 'Content-Type': 'application/json' };
    const produtos = await request.get(
      '/api/v1/items/?family=' + encodeURIComponent('EPI CENARIO E2E'), { headers });
    const [produto] = (await produtos.json()).data.items as { id: string }[];
    const criada = await request.post('/api/v1/material-requisitions/', {
      headers,
      data: { costCenter: 'E2E-001', items: [{ catalogItemId: produto.id, quantity: 3 }] },
    });
    expect(criada.ok()).toBe(true);

    await abrirAutenticado(page, '/cockpit');
    await expect(page.getByTestId('almoxarifado')).toBeVisible();
    // a fila do estoque e a do centro de custo são números diferentes, e os dois aparecem
    await expect(page.getByTestId('almox-fila')).toBeVisible();
    await expect(page.getByTestId('almox-aprovacao')).toBeVisible();

    const { rolaX, rolaY } = await rolagem(page);
    expect(rolaX).toBe(false);
    expect(rolaY).toBe(false);
  });

  test('é tela de parede: sem menu e sem cabeçalho do app', async ({ page }) => {
    await abrirAutenticado(page, '/cockpit');
    await expect(page.getByTestId('cockpit')).toBeVisible();
    // o menu lateral come área de leitura e ninguém navega numa TV
    await expect(page.locator('nav')).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Sair' })).toHaveCount(0);
  });

  test('mostra os quatro níveis e o batimento do ciclo', async ({ page }) => {
    await abrirAutenticado(page, '/cockpit');
    await expect(page.getByTestId('pulso-ao-vivo')).toBeVisible();   // nível 1
    await expect(page.getByTestId('esteira')).toBeVisible();          // nível 3
    // nível 2: os cinco cartões de comando
    await expect(page.getByText('Risco operacional')).toBeVisible();
    await expect(page.getByText('Backlog de suprimentos')).toBeVisible();
    await expect(page.getByText('SLA da semana')).toBeVisible();
    await expect(page.getByText('Saving do mês')).toBeVisible();
    await expect(page.getByText('OTIF 30 dias')).toBeVisible();
    // nível 4: os dois painéis
    await expect(page.getByText('Radar de exceções — ação imediata')).toBeVisible();
    await expect(page.getByText('Produtividade do dia')).toBeVisible();
    await expect(page.getByText('Descargas previstas')).toBeVisible();
  });

  test('o ciclo se repete sozinho, sem recarregar a página', async ({ page }) => {
    await abrirAutenticado(page, '/cockpit');
    await expect(page.getByTestId('cockpit')).toBeVisible();
    // a segunda leitura é a prova do polling: a primeira já aconteceu na abertura
    const segunda = page.waitForResponse(
      (r) => r.url().includes('/api/v1/control-tower/cockpit'), { timeout: 45_000 });
    await expect(async () => { await segunda; }).toPass({ timeout: 45_000 });
    // e a tela continua a mesma instância: nada de reload
    await expect(page.getByTestId('cockpit')).toBeVisible();
  });
});

/**
 * As portas do cockpit. Os testes acima abrem `/cockpit` com o token já injetado, e
 * por isso nunca viram o defeito que o usuário viu: clicar no cockpit abria a aba nova
 * na tela de login. A sessão mora no `sessionStorage`, que é por aba, e o navegador só
 * o copia para a aba nova quando ela mantém o `opener` — o `target="_blank"` hoje
 * implica `noopener`. Aqui a aba nova **não** recebe token nenhum: ou ela herda a
 * sessão de quem a abriu, ou cai no login e o teste falha.
 */
test.describe('Cockpit — abrir a partir do app', () => {
  test.use({ viewport: { width: 1920, height: 1080 } });

  test('pelo menu, a aba nova chega logada', async ({ page, context }) => {
    await abrirAutenticado(page, '/painel');
    const [tv] = await Promise.all([
      context.waitForEvent('page'),
      page.getByRole('link', { name: /Cockpit \(Modo TV\)/ }).click(),
    ]);
    await expect(tv).toHaveURL(/\/cockpit$/);
    await expect(tv.getByTestId('cockpit')).toBeVisible();
    await expect(tv.getByRole('button', { name: /entrar/i })).toHaveCount(0);
  });

  test('pelo "Modo TV" da Torre, a aba nova chega logada', async ({ page, context }) => {
    await abrirAutenticado(page, '/torre');
    const [tv] = await Promise.all([
      context.waitForEvent('page'),
      page.getByRole('link', { name: 'Modo TV ↗' }).click(),
    ]);
    await expect(tv).toHaveURL(/\/cockpit$/);
    await expect(tv.getByTestId('cockpit')).toBeVisible();
  });
});
