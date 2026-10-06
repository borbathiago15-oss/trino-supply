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
  /** A família é de almoxarifado: os produtos dela aparecem em Solicitar Material. */
  materialRequestable: boolean;
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
  materialRequestable: boolean;
}

const base = '/api/v1/product-families';

/** `material` traz só as famílias de almoxarifado — a lista da tela Solicitar Material. */
export const listarFamilias = async (incluirInativas = false, signal?: AbortSignal, material = false) => {
  const params = new URLSearchParams();
  if (incluirInativas) params.set('all', 'true');
  if (material) params.set('material', 'true');
  const query = params.toString();
  return (await api<{ items: Familia[] }>(`${base}/${query ? `?${query}` : ''}`, { signal })).items;
};

export const criarFamilia = (dados: DadosFamilia) => api<Familia>(`${base}/`, { method: 'POST', body: dados });

export const atualizarFamilia = (id: string, dados: Partial<DadosFamilia> & { active?: boolean }) =>
  api<Familia>(`${base}/${id}`, { method: 'PATCH', body: dados });

/** Excluir a família — só vazia, contando os produtos inativos (IC-ERR-031). */
export const excluirFamilia = (id: string) => api<{ deleted: boolean }>(`${base}/${id}`, { method: 'DELETE' });
