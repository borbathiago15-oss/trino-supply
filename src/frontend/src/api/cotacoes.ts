import { api } from './cliente';

/**
 * Situações do processo de cotação, com as chaves que o backend emite
 * (`QStatusLabel`) e os rótulos que o time já lê no sistema clássico.
 */
export const ROTULO_RFQ: Record<string, { rotulo: string; classe: string }> = {
  COTACAO_ABERTA: { rotulo: 'Aguardando propostas', classe: 'bg-slate-100 text-slate-600' },
  EM_ANALISE: { rotulo: 'Em análise', classe: 'bg-blue-50 text-blue-800' },
  AGUARDANDO_GERENTE: { rotulo: 'Aguardando Aprovador 01', classe: 'bg-aviso-fundo text-aviso' },
  AGUARDANDO_DIRETOR: { rotulo: 'Aguardando Aprovador 02', classe: 'bg-aviso-fundo text-aviso' },
  APROVADO_PARA_EMISSAO: { rotulo: 'Aprovado — registrar O.C.', classe: 'bg-teal-50 text-teal-800' },
  OC_REGISTRADA: { rotulo: 'O.C. registrada', classe: 'bg-ok-fundo text-ok' },
  REJEITADO: { rotulo: 'Rejeitado', classe: 'bg-perigo-fundo text-perigo' },
  CANCELADA: { rotulo: 'Cancelada', classe: 'bg-slate-100 text-slate-500' },
};

export interface PropostaResumo { id: string; supplierName: string; totalValue: number | null }

export interface ProcessoParaAprovar {
  id: string;
  number: string;
  status: string;
  costCenter: string;
  sourcePrNumber: string | null;
  justification: string | null;
  selection: { winnerProposalId: string; justification: string | null } | null;
  proposals: PropostaResumo[];
}

/** Processos cotados aguardando a alçada de quem está logado. */
export const processosParaMinhaAprovacao = async (signal?: AbortSignal) =>
  (await api<{ items: ProcessoParaAprovar[] }>('/api/v1/quotations/my-approvals', { signal })).items;

/** Proposta vencedora escolhida pelo comprador, quando já houver seleção. */
export function propostaVencedora(q: ProcessoParaAprovar): PropostaResumo | null {
  if (!q.selection) return null;
  return q.proposals?.find((p) => p.id === q.selection!.winnerProposalId) ?? null;
}

// ---- fila de solicitações aguardando cotação ------------------------------

export type TipoCotacao = 'COMPRA' | 'SERVICO' | 'BID';

export const ROTULO_TIPO: Record<TipoCotacao, string> = {
  COMPRA: 'Cotação de compra',
  SERVICO: 'Cotação de serviço',
  BID: 'BID',
};

export interface ItemDaFila {
  id: string;
  sequence: number;
  catalogCode: string | null;
  description: string;
  quantity: number;
  unitOfMeasure: string | null;
  estimatedUnitPrice: number | null;
  family: string;
}

export interface ScNaFila {
  id: string;
  number: string;
  requesterLabel: string;
  costCenter: string;
  justification: string | null;
  totalEstimatedValue: number;
  neededBy: string | null;
  assignedToId: string | null;
  assignedToLabel: string | null;
  /** Preenchido quando a SC está retida: diz de quem é a aprovação pendente. */
  blockReason: string | null;
  /** Parte da SC já foi para um processo; o que sobrou continua cotável. */
  partial: boolean;
  families: string[];
  items: ItemDaFila[];
}

export const filaDeCotacao = async (signal?: AbortSignal) =>
  ((await api<{ items: ScNaFila[] }>('/api/v1/quotations/queue', { signal })).items ?? [])
    .map((r) => ({ ...r, items: r.items ?? [], families: r.families ?? [] }));

export interface NovoProcesso {
  prItemIds: string[];
  kind: TipoCotacao;
  deadline: string | null;
  notes?: string | null;
}

export const abrirProcesso = (dados: NovoProcesso) =>
  api<{ id: string; number: string }>('/api/v1/quotations/', { method: 'POST', body: dados });

/**
 * Itens marcados na fila, agrupados por família — a base das duas ações da
 * tela: um processo só, ou um processo por família.
 */
export function agruparPorFamilia(marcados: { id: string; familia: string }[]) {
  const grupos = new Map<string, string[]>();
  for (const m of marcados) {
    const atual = grupos.get(m.familia) ?? [];
    atual.push(m.id);
    grupos.set(m.familia, atual);
  }
  return grupos;
}

/**
 * O que a tela pode fazer com a seleção atual. Itens de centros de custo
 * diferentes não entram no mesmo processo — o backend recusa, e o legado já
 * avisava antes de tentar.
 */
export function situacaoDaSelecao(marcados: { id: string; centroCusto: string; familia: string }[]) {
  const centros = new Set(marcados.map((m) => m.centroCusto.toUpperCase()));
  const familias = new Set(marcados.map((m) => m.familia));
  const misturado = centros.size > 1;
  return {
    total: marcados.length,
    familias: familias.size,
    misturado,
    podeJuntar: marcados.length > 0 && !misturado,
    /** Separar só faz sentido com duas famílias ou mais. */
    podeSeparar: marcados.length > 0 && !misturado && familias.size > 1,
    aviso: misturado ? 'Centros de custo diferentes — desmarque para juntar num processo.' : null,
  };
}

// ---- processo de cotação (detalhe) ----------------------------------------

export interface ItemDoProcesso {
  id: string;
  sequence: number;
  catalogCode: string | null;
  description: string;
  quantity: number;
  unitOfMeasure: string;
  sourcePrNumber: string | null;
  family: string;
}

export interface FornecedorConvidado {
  supplierId: string;
  supplierName: string;
  taxId: string;
  invitedAt: string;
  invitedByLabel: string | null;
  hasProposal: boolean;
}

export interface ItemDaProposta { quotationItemId: string; unitPrice: number; quantity: number }

export interface Proposta {
  id: string;
  supplierId: string;
  supplierName: string;
  version: number;
  totalValue: number;
  deliveryDays: number | null;
  paymentTerms: string | null;
  paymentDays: number | null;
  freightValue: number | null;
  taxValue: number | null;
  otherCosts: number | null;
  discountValue: number | null;
  validUntil: string | null;
  currency: string | null;
  notes: string | null;
  submittedVia: string;
  submittedByLabel: string | null;
  submittedAt: string;
  attachmentDocumentId: string | null;
  attachmentFileName: string | null;
  /** Só a última versão de cada fornecedor entra no mapa de comparação. */
  isLatest: boolean;
  isWinner: boolean;
  items: ItemDaProposta[];
}

export interface Adjudicacao {
  id: string;
  family: string;
  supplierId: string;
  supplierName: string;
  proposalId: string;
  proposalVersion: number;
  itemsValue: number;
  totalValue: number;
  criteria: string | null;
  justification: string | null;
  byLabel: string | null;
  purchaseOrderId: string | null;
  purchaseOrderNumber: string | null;
}

export interface OcPendente {
  supplierId: string;
  supplierName: string;
  families: string[];
  totalValue: number;
}

export interface OcDoProcesso {
  id: string;
  number: string | null;
  supplierName: string;
  families: string[];
  totalValue: number;
}

export interface GanhoNegociado {
  baselineValue: number;
  closedValue: number;
  value: number;
  percent: number | null;
  notes: string | null;
  byLabel: string | null;
  at: string;
}

export interface Processo {
  id: string;
  number: string;
  kind: string;
  status: string;
  sourcePrNumber: string | null;
  sourcePrNumbers: string[];
  costCenter: string;
  justification: string | null;
  deadline: string | null;
  notes: string | null;
  createdByLabel: string | null;
  createdAt: string;
  decisionReason: string | null;
  items: ItemDoProcesso[];
  families: string[];
  suppliers: FornecedorConvidado[];
  proposals: Proposta[];
  selection: { winnerSupplierId: string; winnerProposalId: string; criteria: string | null; justification: string; byLabel: string | null } | null;
  managerApproval: { byLabel: string | null; at: string } | null;
  directorApproval: { byLabel: string | null; at: string } | null;
  awards: Adjudicacao[];
  /** A compra ficou com mais de um fornecedor. */
  splitAward: boolean;
  pendingPoSuppliers: OcPendente[];
  purchaseOrders: OcDoProcesso[];
  purchaseOrderId: string | null;
  purchaseOrderNumber: string | null;
  saving: GanhoNegociado | null;
}

/** Critérios oferecidos na escolha do vencedor — os mesmos do sistema clássico. */
export const CRITERIOS = [
  'Preço', 'Prazo de entrega', 'Condição de pagamento', 'Atendimento aos requisitos',
  'Qualidade', 'Condição comercial', 'Histórico do fornecedor',
] as const;

const base = '/api/v1/quotations';

const normalizar = (q: Processo): Processo => ({
  ...q,
  items: q.items ?? [], families: q.families ?? [], suppliers: q.suppliers ?? [],
  proposals: (q.proposals ?? []).map((p) => ({ ...p, items: p.items ?? [] })),
  awards: q.awards ?? [], pendingPoSuppliers: q.pendingPoSuppliers ?? [],
  purchaseOrders: q.purchaseOrders ?? [], sourcePrNumbers: q.sourcePrNumbers ?? [],
});

export const listarProcessos = async (signal?: AbortSignal) =>
  ((await api<{ items: Processo[] }>(`${base}/`, { signal })).items ?? []).map(normalizar);

export const lerProcesso = async (id: string, signal?: AbortSignal) =>
  normalizar(await api<Processo>(`${base}/${id}`, { signal }));

export const convidarFornecedor = (id: string, supplierIds: string[]) =>
  api<Processo>(`${base}/${id}/suppliers`, { method: 'POST', body: { supplierIds } });

export interface PropostaManual {
  supplierId: string;
  deliveryDays: number | null;
  paymentTerms: string | null;
  paymentDays: number | null;
  freightValue: number | null;
  taxValue: number | null;
  otherCosts: number | null;
  discountValue: number | null;
  validUntil: string | null;
  currency: string;
  notes: string | null;
  items: { quotationItemId: string; unitPrice: number }[];
}

export const registrarProposta = (id: string, dados: PropostaManual) =>
  api<Processo>(`${base}/${id}/proposals`, { method: 'POST', body: dados });

export const encerrarParaAnalise = (id: string) =>
  api<Processo>(`${base}/${id}/close`, { method: 'POST' });

export interface EscolhaDoVencedor {
  proposalId: string;
  criteria: string[];
  justification: string;
  /** Compra dividida: um vencedor por família. */
  awards?: { family: string; proposalId: string; criteria: string[]; justification: string | null }[];
}

export const escolherVencedor = (id: string, escolha: EscolhaDoVencedor) =>
  api<Processo>(`${base}/${id}/select-winner`, { method: 'POST', body: escolha });

export type Decisao = 'APROVAR' | 'AJUSTES' | 'REJEITAR';
export type Alcada = 'manager' | 'director';

export const decidir = (id: string, alcada: Alcada, decision: Decisao, reason: string | null) =>
  api<Processo>(`${base}/${id}/${alcada}-decision`, { method: 'POST', body: { decision, reason } });

export interface RegistroDeOc {
  erpNumber: string;
  issuedOn: string | null;
  notes: string | null;
  supplierId?: string | null;
  overLimitJustification?: string | null;
}

export const registrarOc = (id: string, dados: RegistroDeOc) =>
  api<Processo>(`${base}/${id}/register-po`, { method: 'POST', body: dados });

export interface Negociacao {
  supplierId: string;
  closedValue: number | null;
  discountPercent: number | null;
  notes: string | null;
}

export const registrarNegociacao = (id: string, dados: Negociacao) =>
  api<Processo>(`${base}/${id}/negotiation`, { method: 'POST', body: dados });

export const cancelarProcesso = (id: string, reason: string) =>
  api<Processo>(`${base}/${id}/cancel`, { method: 'POST', body: { reason } });

// ---- leituras do mapa de comparação ---------------------------------------

/** Só a última versão de cada fornecedor entra na comparação. */
export const propostasVigentes = (q: Processo) => q.proposals.filter((p) => p.isLatest);

/** Preço unitário de um item numa proposta, ou null quando não foi cotado. */
export const precoDoItem = (p: Proposta, itemId: string) =>
  p.items.find((x) => x.quotationItemId === itemId)?.unitPrice ?? null;

/** Menor preço de um item entre as propostas vigentes — destacado no mapa. */
export function melhorPreco(q: Processo, itemId: string): number | null {
  const precos = propostasVigentes(q).map((p) => precoDoItem(p, itemId)).filter((v): v is number => v != null);
  return precos.length ? Math.min(...precos) : null;
}

/** Menor total entre as propostas vigentes. Com uma proposta só, não há o que comparar. */
export function menorTotal(q: Processo): number | null {
  const vigentes = propostasVigentes(q);
  return vigentes.length > 1 ? Math.min(...vigentes.map((p) => p.totalValue)) : null;
}

/** O que o usuário pode fazer no processo, dado o papel e a etapa. */
export function acoesDisponiveis(
  q: Processo,
  { conduz, aprovaNivel1, aprovaNivel2 }: { conduz: boolean; aprovaNivel1: boolean; aprovaNivel2: boolean },
) {
  const vigentes = propostasVigentes(q);
  const emAberto = q.status === 'COTACAO_ABERTA';
  const emAnalise = q.status === 'EM_ANALISE';
  return {
    convidar: conduz && (emAberto || emAnalise),
    registrarProposta: conduz && (emAberto || emAnalise) && q.suppliers.length > 0,
    negociar: conduz && (emAberto || emAnalise) && vigentes.length > 0,
    encerrar: conduz && emAberto,
    escolherVencedor: conduz && emAnalise && vigentes.length > 0,
    /** Mais de uma família: escolhe-se um fornecedor por família. */
    porFamilia: q.families.length > 1,
    decidirNivel1: aprovaNivel1 && q.status === 'AGUARDANDO_GERENTE',
    decidirNivel2: aprovaNivel2 && q.status === 'AGUARDANDO_DIRETOR',
    registrarOc: conduz && q.status === 'APROVADO_PARA_EMISSAO',
    cancelar: conduz && !['OC_REGISTRADA', 'REJEITADO', 'CANCELADA'].includes(q.status),
  };
}
