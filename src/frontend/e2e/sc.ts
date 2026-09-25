import type { Page } from '@playwright/test';

/**
 * Item fora do catálogo na Inclusão de SC. A linha não tem mais campo de texto: o caminho é
 * buscar no catálogo e, sem achar, pedir o item fora dele — com o termo buscado como descrição.
 */
export async function itemForaDoCatalogo(page: Page, descricao: string, quantidade: string, unidade?: string) {
  await page.getByRole('button', { name: 'Buscar no catálogo' }).first().click();
  const dialogo = page.getByRole('dialog', { name: 'Escolher produto do catálogo' });
  await dialogo.getByLabel(/Buscar produto/).fill(descricao);
  await dialogo.getByRole('button', { name: 'Não achou? Pedir item fora do catálogo' }).click();
  if (unidade) await page.getByLabel('Unidade').first().fill(unidade);
  await page.getByLabel('Quantidade').first().fill(quantidade);
}
