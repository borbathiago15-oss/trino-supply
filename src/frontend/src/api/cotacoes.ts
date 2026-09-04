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
