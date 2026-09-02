import { api } from './cliente';

/** Situações do processo de cotação (RFQ-001). */
export const ROTULO_RFQ: Record<string, { rotulo: string; classe: string }> = {
  RASCUNHO: { rotulo: 'Rascunho', classe: 'bg-slate-100 text-slate-600' },
  ABERTA: { rotulo: 'Aberta', classe: 'bg-blue-50 text-blue-800' },
  EM_ANALISE: { rotulo: 'Em análise', classe: 'bg-blue-50 text-blue-800' },
  AGUARDANDO_APROVACAO: { rotulo: 'Aguardando aprovação', classe: 'bg-aviso-fundo text-aviso' },
  AGUARDANDO_DIRETORIA: { rotulo: 'Aguardando diretoria', classe: 'bg-aviso-fundo text-aviso' },
  APROVADA: { rotulo: 'Aprovada', classe: 'bg-ok-fundo text-ok' },
  OC_REGISTRADA: { rotulo: 'O.C. registrada', classe: 'bg-ok-fundo text-ok' },
  REPROVADA: { rotulo: 'Reprovada', classe: 'bg-slate-100 text-slate-500' },
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
