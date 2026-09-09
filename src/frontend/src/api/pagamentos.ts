import { api } from './cliente';

/** Forma de pagamento: por onde o dinheiro sai (boleto, Pix, depósito…). */
export interface FormaDePagamento {
  id: string;
  name: string;
  active: boolean;
}

/**
 * Condição de pagamento: quando se paga. `installments` é em quantas parcelas e
 * `firstDueDays` é o prazo até a primeira — é ele que preenche sozinho o campo
 * "prazo p/ pagamento" da proposta, que hoje o comprador redigita a cada cotação.
 */
export interface CondicaoDePagamento {
  id: string;
  name: string;
  installments: number;
  firstDueDays: number | null;
  isDefault: boolean;
  active: boolean;
}

const formas = '/api/v1/payment-methods';
const condicoes = '/api/v1/payment-terms';

export const listarFormasDePagamento = async (incluirInativas = false, signal?: AbortSignal) =>
  (await api<{ items: FormaDePagamento[] }>(`${formas}/${incluirInativas ? '?all=true' : ''}`, { signal })).items;

export const criarFormaDePagamento = (name: string) =>
  api<FormaDePagamento>(`${formas}/`, { method: 'POST', body: { name } });

export const atualizarFormaDePagamento = (id: string, dados: { name?: string; active?: boolean }) =>
  api<FormaDePagamento>(`${formas}/${id}`, { method: 'PATCH', body: dados });

export const listarCondicoesDePagamento = async (incluirInativas = false, signal?: AbortSignal) =>
  (await api<{ items: CondicaoDePagamento[] }>(`${condicoes}/${incluirInativas ? '?all=true' : ''}`, { signal })).items;

export const criarCondicaoDePagamento = (dados: {
  name: string; installments: number; firstDueDays: number | null; isDefault: boolean;
}) => api<CondicaoDePagamento>(`${condicoes}/`, { method: 'POST', body: dados });

export const atualizarCondicaoDePagamento = (id: string, dados: {
  name?: string; installments?: number; firstDueDays?: number | null;
  isDefault?: boolean; active?: boolean;
}) => api<CondicaoDePagamento>(`${condicoes}/${id}`, { method: 'PATCH', body: dados });
