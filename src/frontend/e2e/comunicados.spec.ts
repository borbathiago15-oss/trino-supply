import { expect, test } from '@playwright/test';
import { abrirAutenticado } from './sessao';

/**
 * Comunicados: o administrador publica, quem abre o sistema vê, fecha, e não vê
 * de novo. O "não vê de novo" é o que só um teste de ponta a ponta prova — ele
 * depende de o fechamento ter chegado ao servidor, e não de o modal ter sumido.
 */
test.describe('Comunicados (React)', () => {
  test('publicar, aparecer ao abrir o sistema, fechar e não voltar', async ({ page }) => {
    const titulo = `E2E comunicado ${Date.now()}`;
    const hoje = new Date().toISOString().slice(0, 10);

    await abrirAutenticado(page, '/comunicados');
    await expect(page.locator('#titulo-pagina')).toHaveText('Comunicados');

    // pelos ids, como as outras specs: `getByLabel` do Playwright casa substring
    // sem diferenciar maiúsculas, e "Até" casaria também com "…até 10 MB" da imagem
    await page.locator('#com-titulo').fill(titulo);
    await page.locator('#com-de').fill(hoje);
    await page.locator('#com-ate').fill(hoje);
    await page.locator('#com-texto').fill('Recado do teste de ponta a ponta.');

    const publicacao = page.waitForResponse((r) =>
      r.url().includes('/api/v1/announcements/') && r.request().method() === 'POST');
    await page.getByRole('button', { name: 'Publicar comunicado' }).click();
    expect((await publicacao).status()).toBe(201);

    // a lista já mostra o comunicado no ar, com a vigência de hoje
    const linha = page.getByTestId('tabela-comunicados').locator('tr', { hasText: titulo }).first();
    await expect(linha).toBeVisible();
    await expect(linha).toContainText('NO AR');

    // ao abrir qualquer tela, o recado vem por cima
    await abrirAutenticado(page, '/painel');
    const modal = page.getByTestId('comunicado');
    await expect(modal).toBeVisible();
    await expect(modal).toContainText(titulo);

    const fechamento = page.waitForResponse((r) => r.url().includes('/dismiss'));
    await modal.getByRole('button', { name: 'Entendi, fechar' }).click();
    expect((await fechamento).status()).toBe(200);
    await expect(modal).toBeHidden();

    // e não volta na próxima abertura: o fechamento ficou no servidor
    await abrirAutenticado(page, '/painel');
    await expect(page.locator('#titulo-pagina')).toBeVisible();
    await expect(page.getByTestId('comunicado')).toBeHidden();

    // limpeza: o cenário não pode deixar recado no caminho das outras specs
    await abrirAutenticado(page, '/comunicados');
    page.once('dialog', (d) => d.accept());
    await page.getByTestId('tabela-comunicados').locator('tr', { hasText: titulo }).first()
      .getByRole('button', { name: 'Excluir' }).click();
    await expect(page.getByTestId('tabela-comunicados').locator('tr', { hasText: titulo })).toHaveCount(0);
  });
});
