import { api } from './cliente';

/**
 * Situação da solicitação de material — os mesmos rótulos do `MrView` do
 * backend. O legado só nomeava cinco: `AGUARDANDO_APROVACAO` e `RECUSADA`
 * caíam na tela como o código cru.
 */
export type SituacaoMaterial =
  | 'AGUARDANDO_APROVACAO' | 'AGUARDANDO_ALMOXARIFADO'
  | 'ATENDIDA' | 'ATENDIDA_PARCIAL' | 'ROTA_DE_COMPRA' | 'RECUSADA' | 'CANCELADA';

export const ROTULO_MATERIAL: Record<SituacaoMaterial, { rotulo: string; classe: string }> = {
  AGUARDANDO_APROVACAO: { rotulo: 'Aguardando aprovação', classe: 'bg-aviso-fundo text-aviso' },
  AGUARDANDO_ALMOXARIFADO: { rotulo: 'Aguardando almoxarifado', classe: 'bg-slate-100 text-slate-600' },
  ATENDIDA: { rotulo: 'Atendida', classe: 'bg-ok-fundo text-ok' },
  ATENDIDA_PARCIAL: { rotulo: 'Atendida parcialmente', classe: 'bg-blue-50 text-blue-800' },
  ROTA_DE_COMPRA: { rotulo: 'Rota de compra', classe: 'bg-blue-50 text-blue-800' },
  RECUSADA: { rotulo: 'Recusada', classe: 'bg-perigo-fundo text-perigo' },
  CANCELADA: { rotulo: 'Cancelada', classe: 'bg-slate-100 text-slate-500' },
};

export type SituacaoItemMaterial = 'PENDENTE' | 'ENTREGUE' | 'ENTREGUE_PARCIAL' | 'ROTA_DE_COMPRA';

export const ROTULO_ITEM_MATERIAL: Record<SituacaoItemMaterial, string> = {
  PENDENTE: 'pendente',
  ENTREGUE: 'entregue ✓',
  ENTREGUE_PARCIAL: 'entregue parcialmente',
  ROTA_DE_COMPRA: 'rota de compra',
};

export interface ItemMaterial {
  itemId: string;
  catalogCode?: string | null;
  description: string;
  quantity: number;
  unitOfMeasure: string;
  approvedQuantity?: number | null;
  fulfilledQuantity?: number;
  pendingQuantity?: number;
  status?: SituacaoItemMaterial;
}

export interface SolicitacaoMaterial {
  id: string;
  number: string;
  status: SituacaoMaterial;
  costCenter: string;
  requesterId?: string;
  requesterLabel: string;
  notes: string | null;
  fulfilledByLabel?: string | null;
  purchaseRequisitionNumber?: string | null;
  cancelReason?: string | null;
  decisionReason?: string | null;
  items: ItemMaterial[];
}

const base = '/api/v1/material-requisitions';

export const listarSolicitacoesMaterial = async (signal?: AbortSignal) =>
  (await api<{ items: SolicitacaoMaterial[] }>(`${base}/`, { signal })).items;

export interface NovaSolicitacaoMaterial {
  costCenter: string;
  notes: string | null;
  items: { catalogItemId: string; quantity: number }[];
}

export const criarSolicitacaoMaterial = (dados: NovaSolicitacaoMaterial) =>
  api<SolicitacaoMaterial>(`${base}/`, { method: 'POST', body: dados });

/** Aprovação de Nível 1: libera tudo ou ajusta a quantidade item a item. */
export const aprovarMaterial = (id: string, items: { itemId: string; quantity: number }[], notes: string | null) =>
  api<unknown>(`${base}/${id}/approve`, { method: 'POST', body: { items, notes } });

export const recusarMaterial = (id: string, reason: string) =>
  api<unknown>(`${base}/${id}/reject`, { method: 'POST', body: { reason } });

export const cancelarMaterial = (id: string, reason: string) =>
  api<unknown>(`${base}/${id}/cancel`, { method: 'POST', body: { reason } });

/**
 * Quem pediu só cancela enquanto o almoxarifado não mexeu — a mesma regra que
 * o legado aplicava ao desenhar o botão.
 */
export const podeCancelarMaterial = (r: SolicitacaoMaterial, usuarioId: string) =>
  (r.status === 'AGUARDANDO_ALMOXARIFADO' || r.status === 'AGUARDANDO_APROVACAO')
  && r.requesterId === usuarioId;
