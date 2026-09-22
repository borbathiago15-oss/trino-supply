import { api } from './cliente';

/**
 * Setor da casa — RH, TI, Manutenção. Não é centro de custo: o centro é onde o dinheiro
 * cai, o setor é quem trabalha. Um setor atende vários centros, e um centro é atendido
 * por vários setores.
 */
export interface Setor {
  id: string;
  code: string;
  name: string;
  active: boolean;
}

const base = '/api/v1/sectors';

export const listarSetores = async (incluirInativos = false, signal?: AbortSignal) =>
  (await api<{ items: Setor[] }>(`${base}/${incluirInativos ? '?all=true' : ''}`, { signal })).items;

export const criarSetor = (dados: { name: string; code?: string }) =>
  api<Setor>(`${base}/`, { method: 'POST', body: dados });

/** O código é identidade e não muda: pessoas e ciclos já o carregam. */
export const atualizarSetor = (id: string, dados: { name?: string; active?: boolean }) =>
  api<Setor>(`${base}/${id}`, { method: 'PATCH', body: dados });
