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
