import { api } from './cliente';
import { moeda } from '@/util/formato';
import type { SlaDoAtendimento } from './prazoDeAtendimento';

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
  /** O que vale para o atendimento: o aprovado quando houve corte, senão o pedido. */
  effectiveQuantity?: number;
  fulfilledQuantity?: number;
  pendingQuantity?: number;
  status?: SituacaoItemMaterial;
  /** O custo de compra congelado no dia do pedido; nulo no produto sem custo e na solicitação anterior à regra. */
  unitPrice?: number | null;
  requestedValue?: number | null;
  approvedValue?: number | null;
  fulfilledValue?: number | null;
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
  approvedByLabel?: string | null;
  assignedToId?: string | null;
  assignedToLabel?: string | null;
  purchaseRequisitionNumber?: string | null;
  cancelReason?: string | null;
  decisionReason?: string | null;
  items: ItemMaterial[];
  /** Os três valores da solicitação (pedido, liberado, entregue), pelo custo congelado; nulos sem custo. */
  requestedValue?: number | null;
  approvedValue?: number | null;
  fulfilledValue?: number | null;
  /** Itens sem custo: separa "vale zero" de "ninguém cadastrou o custo". */
  itemsWithoutPrice?: number;
  /** O prazo de atendimento da família mais curta entre os itens — derivado no servidor. */
  sla?: SlaDoAtendimento | null;
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
/**
 * O valor da solicitação dito em uma frase — e o que falta para ele ser inteiro. Nulo quando não
 * há nada a dizer (nenhum item com custo e nenhum sem): zero diria que o material é de graça.
 */
export function fraseDoValor(valor: number | null | undefined, semCusto = 0): string | null {
  if (valor == null) return semCusto > 0 ? 'sem custo cadastrado' : null;
  const texto = moeda(valor);
  return semCusto > 0 ? `${texto} (${semCusto} ${semCusto === 1 ? 'item' : 'itens'} sem custo)` : texto;
}

export const podeCancelarMaterial = (r: SolicitacaoMaterial, usuarioId: string) =>
  (r.status === 'AGUARDANDO_ALMOXARIFADO' || r.status === 'AGUARDANDO_APROVACAO')
  && r.requesterId === usuarioId;

// ---- fila e painel do almoxarifado ----------------------------------------

/** Só entra na fila o que o responsável do centro já aprovou. */
export const filaDoAlmoxarifado = async (somenteMinhas: boolean, signal?: AbortSignal) =>
  (await api<{ items: SolicitacaoMaterial[] }>(
    `${base}/?queue=true${somenteMinhas ? '&mine=true' : ''}`, { signal })).items;

/**
 * Atendimento: quantidade entregue por item. O que faltar vira solicitação de
 * compra no nome de quem pediu — por isso a resposta traz o número da SC.
 */
export interface DecisaoDoAtendimento {
  /** Encerra o atendimento mesmo parcial; `false` deixa a solicitação na fila do estoque. */
  concluir: boolean;
  /** Abre a SC do que faltou, no nome de quem pediu. */
  gerarCompra: boolean;
}

export const atenderMaterial = (
  id: string, items: { itemId: string; quantity: number }[], decisao: DecisaoDoAtendimento,
) => api<SolicitacaoMaterial>(`${base}/${id}/fulfill`, { method: 'POST', body: { items, ...decisao } });

export interface LinhaPainel {
  id: string;
  number: string;
  costCenter: string;
  requesterLabel: string;
  createdAt: string;
  approvedAt: string | null;
  fulfilledAt: string | null;
  fulfilledByLabel: string | null;
  purchaseRequisitionNumber: string | null;
  items: number;
  pending: number;
  summary: string | null;
  /** O valor liberado (o pedido, enquanto o centro não decide); nulo sem custo. */
  value?: number | null;
  itemsWithoutPrice?: number;
  sla?: SlaDoAtendimento | null;
}

export interface GrupoPainel {
  costCenter?: string;
  /** Id do solicitante: é por ele que o filtro recorta — dois "João Silva" são duas pessoas. */
  requesterId?: string;
  requesterLabel?: string;
  total: number;
  emAndamento: number;
  concluidos: number;
  parciais: number;
}

/**
 * O que o filtro oferece para escolher. Vem do **cadastro inteiro**, e não do recorte: a quebra
 * por centro conta o recorte (é o que faz o número bater com a lista), mas o seletor responde
 * outra pergunta — "para onde eu posso ir agora". Tirá-lo do recorte faria do filtro uma porta de
 * mão única: quem filtrasse o BAH-001 veria o seletor passar a oferecer só o BAH-001.
 */
export interface OpcoesDoPainel {
  centros: string[];
  solicitantes: { id: string; label: string }[];
}

export interface PainelAtendimentos {
  totals: { aguardandoAprovacao: number; emAndamento: number; concluidos: number; parciais: number };
  opcoes: OpcoesDoPainel;
  aguardandoAprovacao: LinhaPainel[];
  emAndamento: LinhaPainel[];
  concluidos: LinhaPainel[];
  parciais: LinhaPainel[];
  porCentro: GrupoPainel[];
  porSolicitante: GrupoPainel[];
}

/**
 * O recorte do painel. Vai ao servidor, e não fica no navegador, para o cartão continuar
 * contando exatamente a lista que ele abre: filtrar só as listas deixaria o número do topo
 * dizendo uma coisa e a tabela outra.
 */
export interface FiltroDoPainel {
  costCenter?: string;
  requesterId?: string;
  from?: string;
  to?: string;
}

export const FILTRO_VAZIO: FiltroDoPainel = {};

/** Quantos recortes estão valendo — o número que o botão mostra. */
export const filtrosAtivos = (f: FiltroDoPainel) =>
  [f.costCenter, f.requesterId, f.from, f.to].filter((v) => !!v).length;

export const painelDeAtendimentos = (filtro: FiltroDoPainel = {}, signal?: AbortSignal) => {
  const params = new URLSearchParams();
  if (filtro.costCenter) params.set('costCenter', filtro.costCenter);
  if (filtro.requesterId) params.set('requesterId', filtro.requesterId);
  if (filtro.from) params.set('from', filtro.from);
  if (filtro.to) params.set('to', filtro.to);
  const query = params.toString();
  return api<PainelAtendimentos>(`${base}/panel${query ? `?${query}` : ''}`, { signal });
};

// ---- A solicitação de material no Dashboard e na Visão da diretoria ----------------------

export interface LinhaDeMaterial { label: string; key: string; count: number; qty: number; delivered: number }
export interface MesDeMaterial { month: string; requested: number; fulfilled: number; rejected: number }
export interface PrazoDaFamilia { family: string; maxDays: number | null; measured: number; met: number; avgDays: number | null }
export interface KpisDeMaterial {
  requested: number; requestedPrev: number; requestedQty: number; deliveredQty: number;
  awaitingApproval: number; inWarehouseQueue: number; fulfilled: number; partial: number; rejected: number; cancelled: number;
  purchaseRouteItems: number; avgApprovalHours: number | null; avgFulfillDays: number | null; avgTotalDays: number | null;
  slaMetPercent: number | null; slaMeasured: number; slaBreachedOpen: number;
}
export interface RelatorioDeMaterial {
  from: string; to: string; kpis: KpisDeMaterial; months: MesDeMaterial[];
  byCostCenter: LinhaDeMaterial[]; byFamily: LinhaDeMaterial[]; byProduct: LinhaDeMaterial[]; byRequester: LinhaDeMaterial[];
  slaByFamily: PrazoDaFamilia[];
  filterOptions: { costCenters: { code: string; name: string }[]; families: string[] };
  indicators?: Record<string, string>;
}

/**
 * Os filtros são próprios: período, centro, família e produto. Os do painel de compras falam
 * de fornecedor, comprador e prioridade, que a solicitação de material não tem.
 */
export interface FiltrosMaterial { de: string; ate: string; centroCusto: string; familia: string; produto: string }
export const FILTROS_MATERIAL_VAZIOS: FiltrosMaterial = { de: '', ate: '', centroCusto: '', familia: '', produto: '' };

export function consultaDeMaterial(f: FiltrosMaterial) {
  const params = new URLSearchParams();
  if (f.de) params.set('from', f.de);
  if (f.ate) params.set('to', f.ate);
  if (f.centroCusto) params.set('costCenter', f.centroCusto);
  if (f.familia) params.set('family', f.familia);
  if (f.produto.trim()) params.set('product', f.produto.trim());
  return params.toString();
}

export const analyticsDeMaterial = (f: FiltrosMaterial, signal?: AbortSignal) => {
  const query = consultaDeMaterial(f);
  return api<RelatorioDeMaterial>(`/api/v1/analytics/material${query ? `?${query}` : ''}`, { signal });
};
