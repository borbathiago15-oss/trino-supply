import { expect, test } from '@playwright/test';
import { abrirAutenticado } from './sessao';

/**
 * A meta é da empresa e mora num cadastro do administrador. O teste grava uma meta, confere
 * que ela voltou do servidor e a apaga — vazio é "sem meta", e deixar a meta gravada mudaria a
 * cor dos cards dos outros testes.
 */
test('metas: o administrador grava a meta, ela volta do servidor, e vazio apaga', async ({ page }) => {
  await abrirAutenticado(page, '/metas');
  const otif = page.getByLabel('Entrega no prazo (OTIF)');
  await expect(otif).toBeEnabled();

  await otif.fill('92');
  await page.getByRole('button', { name: 'Salvar metas' }).click();
  await expect(page.getByText('Metas salvas')).toBeVisible();
  await page.reload();
  await expect(page.getByLabel('Entrega no prazo (OTIF)')).toHaveValue('92');

  await page.getByLabel('Entrega no prazo (OTIF)').fill('');
  await page.getByRole('button', { name: 'Salvar metas' }).click();
  await page.reload();
  await expect(page.getByLabel('Entrega no prazo (OTIF)')).toHaveValue('');
});
