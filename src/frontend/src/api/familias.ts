import { api } from './cliente';

/** Família de produtos: agrupa o catálogo e define os prazos-meta do processo. */
export interface Familia {
  id: string;
  name: string;
  notes: string | null;
  active: boolean;
  category: string | null;
  leadRequestToQuote: number | null;
  leadQuoteToApproval: number | null;
  leadApprovalToPo: number | null;
  leadPoToDelivery: number | null;
  leadTotal: number | null;
}

export interface DadosFamilia {
  name: string;
  notes: string | null;
  category: string | null;
  clearCategory?: boolean;
  leadRequestToQuote: number | null;
  leadQuoteToApproval: number | null;
  leadApprovalToPo: number | null;
  leadPoToDelivery: number | null;
  /** O formulário sempre manda as quatro etapas: vazio limpa a meta. */
  applyLeadTimes: true;
}

const base = '/api/v1/product-families';

export const listarFamilias = async (incluirInativas = false, signal?: AbortSignal) =>
  (await api<{ items: Familia[] }>(`${base}/${incluirInativas ? '?all=true' : ''}`, { signal })).items;

export const criarFamilia = (dados: DadosFamilia) => api<Familia>(`${base}/`, { method: 'POST', body: dados });

export const atualizarFamilia = (id: string, dados: Partial<DadosFamilia> & { active?: boolean }) =>
  api<Familia>(`${base}/${id}`, { method: 'PATCH', body: dados });

/** Excluir a família — só vazia, contando os produtos inativos (IC-ERR-031). */
export const excluirFamilia = (id: string) => api<{ deleted: boolean }>(`${base}/${id}`, { method: 'DELETE' });
