import { api } from './cliente';

export type SituacaoMaterial =
  | 'AGUARDANDO_APROVACAO' | 'AGUARDANDO_ALMOXARIFADO' | 'EM_SEPARACAO'
  | 'ATENDIDA' | 'ATENDIDA_PARCIAL' | 'ROTA_COMPRA' | 'RECUSADA' | 'CANCELADA';

export interface ItemMaterial {
  itemId: string;
  description: string;
  quantity: number;
  unitOfMeasure: string;
  approvedQuantity?: number | null;
}

export interface SolicitacaoMaterial {
  id: string;
  number: string;
  status: SituacaoMaterial;
  costCenter: string;
  requesterLabel: string;
  notes: string | null;
  items: ItemMaterial[];
}

const base = '/api/v1/material-requisitions';

export const listarSolicitacoesMaterial = async (signal?: AbortSignal) =>
  (await api<{ items: SolicitacaoMaterial[] }>(`${base}/`, { signal })).items;

/** Aprovação de Nível 1: libera tudo ou ajusta a quantidade item a item. */
export const aprovarMaterial = (id: string, items: { itemId: string; quantity: number }[], notes: string | null) =>
  api<unknown>(`${base}/${id}/approve`, { method: 'POST', body: { items, notes } });

export const recusarMaterial = (id: string, reason: string) =>
  api<unknown>(`${base}/${id}/reject`, { method: 'POST', body: { reason } });
