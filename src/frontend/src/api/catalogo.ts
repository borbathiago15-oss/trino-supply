import { api } from './cliente';

/** Produto do catálogo — aqui só o necessário para escolher em outras telas. */
export interface ProdutoResumo {
  id: string;
  code: string;
  description: string;
  family: string;
  unitOfMeasure: string;
  referencePrice: number | null;
  active: boolean;
}

export const listarProdutos = async (signal?: AbortSignal) =>
  (await api<{ items: ProdutoResumo[] }>('/api/v1/items/', { signal })).items;

/** Quantos produtos existem por família — usado no cadastro de famílias. */
export async function contarProdutosPorFamilia(signal?: AbortSignal): Promise<Record<string, number>> {
  const { items } = await api<{ items: { family: string }[] }>('/api/v1/items/?all=true', { signal });
  return items.reduce<Record<string, number>>((acc, i) => {
    acc[i.family] = (acc[i.family] ?? 0) + 1;
    return acc;
  }, {});
}
