import { api } from './cliente';

export type StatusDaAcao = 'PENDENTE' | 'EM_ANDAMENTO' | 'CONCLUIDA' | 'SUSPENSA' | 'CANCELADA';

export const ROTULO_STATUS: Record<StatusDaAcao, string> = {
  PENDENTE: 'Pendente', EM_ANDAMENTO: 'Em andamento', CONCLUIDA: 'Concluída',
  SUSPENSA: 'Suspensa', CANCELADA: 'Cancelada',
};

/** Suspender e cancelar param o trabalho de alguém: o servidor cobra o motivo (AC-ERR-014). */
export const exigeMotivo = (s: StatusDaAcao) => s === 'SUSPENSA' || s === 'CANCELADA';

export type SituacaoDoPlano =
  'PENDENTE' | 'EM_ANDAMENTO' | 'ATRASADO' | 'CONCLUIDO' | 'CANCELADO';

export const ROTULO_SITUACAO: Record<SituacaoDoPlano, string> = {
  PENDENTE: 'Pendente', EM_ANDAMENTO: 'Em andamento', ATRASADO: 'Atrasado',
  CONCLUIDO: 'Concluído', CANCELADO: 'Cancelado',
};

/** Uma ação dentro de um plano — o 5W2H do que será feito. */
export interface Acao {
  id: string; number: string; seq: number; planId: string; title: string;
  reason: string | null; area: string | null; supportArea: string | null;
  responsibleId: string; responsibleLabel: string;
  startDate: string | null; dueDate: string | null;
  expectedGain: number | null; realizedGain: number | null;
  expectedResult: string | null; kpi: string | null; costCenter: string | null;
  rootCauseRef: string | null; complexity: string; riskLevel: string;
  dependencies: string | null; evidence: string | null; comments: string | null;
  status: StatusDaAcao; statusReason: string | null;
  progress: number; late: boolean; daysLate: number | null; open: boolean;
  completedAt: string | null; createdByLabel: string; createdAt: string;
}

/** O plano — o projeto, com as ações dentro dele. */
export interface Plano {
  id: string; code: string; title: string; description: string | null;
  costCenter: string | null; areas: string[]; otherArea: string | null;
  priority: string; criticality: string; complexity: string;
  category: string | null; sponsor: string | null; managerName: string | null;
  startDate: string | null; dueDate: string | null; completion: number;
  problem: string | null; businessReason: string | null;
  operationalImpact: string | null; financialImpact: string | null;
  kpiAffected: string | null; targetGoal: string | null;
  roiExpected: number;
  savingExpected: number; savingRealized: number;
  investmentPlanned: number; investmentActual: number;
  cancelled: boolean; cancelReason: string | null;
  life: 'ATIVO' | 'ENCERRADO';
  closedAt: string | null; closedByLabel: string | null; evidenceNote: string | null;
  cycleId: string | null; autoKey: string | null;
  responsibles: { userId: string; label: string }[];
  // derivados pelo servidor: a tela não recalcula o que ele já sabe
  status: SituacaoDoPlano;
  itemsProgress: number;
  savingTotalExpected: number; savingTotalRealized: number;
  roi: number | null;
  closedWithPending: boolean;
  itemCount: number; openItems: number;
  createdByLabel: string; createdAt: string;
}

export interface Risco {
  id: string; description: string; probability: string; impact: string;
  score: number; severity: 'BAIXO' | 'MEDIO' | 'ALTO' | 'CRITICO';
  mitigation: string | null; responsibleId: string | null; responsibleLabel: string | null;
  status: string;
}

export interface CausaRaizDoPlano {
  method: string; contentJson: string | null; mainCause: string | null;
}

export interface LicoesDoPlano {
  whatWorked: string | null; whatFailed: string | null; lessons: string | null;
  bestPractice: string | null; nextSteps: string | null; recommendation: string | null;
}

export interface PlacarDosPlanos {
  total: number; pendentes: number; emAndamento: number; atrasados: number;
  concluidos: number; cancelados: number; encerrados: number;
  encerradosComPendencia: number;
  savingEsperado: number; savingRealizado: number;
}

export interface OpcoesDoPlano {
  responsibles: { id: string; label: string }[];
  costCenters: string[];
  areas: string[];
  categories: string[];
  priorities: string[];
  degrees: string[];
  riskDegrees: string[];
  statuses: string[];
}

export interface PlanoCompleto {
  plan: Plano;
  items: Acao[];
  risks: Risco[];
  rootCause: CausaRaizDoPlano | null;
  lessons: LicoesDoPlano | null;
}

const base = '/api/v1/action-plans';

export interface FiltroDePlanos {
  busca?: string; status?: string; prioridade?: string; centroCusto?: string;
  area?: string; responsavel?: string; encerrados?: boolean; cicloId?: string;
}

export const listarPlanos = (f: FiltroDePlanos = {}, signal?: AbortSignal) => {
  const p = new URLSearchParams();
  if (f.busca) p.set('q', f.busca);
  if (f.status) p.set('status', f.status);
  if (f.prioridade) p.set('priority', f.prioridade);
  if (f.centroCusto) p.set('costCenter', f.centroCusto);
  if (f.area) p.set('area', f.area);
  if (f.responsavel) p.set('responsibleId', f.responsavel);
  if (f.encerrados !== undefined) p.set('closed', String(f.encerrados));
  if (f.cicloId) p.set('cycleId', f.cicloId);
  const qs = p.toString();
  return api<{ items: Plano[]; placar: PlacarDosPlanos; filterOptions: OpcoesDoPlano }>(
    `${base}/${qs ? '?' + qs : ''}`, { signal });
};

export const abrirPlano = (id: string, signal?: AbortSignal) =>
  api<PlanoCompleto>(`${base}/${id}`, { signal });

export type DadosDoPlano = Partial<{
  title: string; description: string | null; costCenter: string | null;
  areas: string[]; otherArea: string | null; priority: string;
  startDate: string | null; dueDate: string | null; completion: number;
  problem: string | null; businessReason: string | null; category: string | null;
  sponsor: string | null; managerName: string | null;
  operationalImpact: string | null; financialImpact: string | null;
  kpiAffected: string | null; targetGoal: string | null;
  criticality: string; complexity: string;
  roiExpected: number; savingExpected: number; savingRealized: number;
  investmentPlanned: number; investmentActual: number;
  responsibleIds: string[]; cycleId: string | null;
  cancelled: boolean; cancelReason: string | null;
}>;

export const criarPlano = (dados: DadosDoPlano) =>
  api<Plano>(`${base}/`, { method: 'POST', body: dados });

/** Plano encerrado não se edita (AP-ERR-020): a tela reabre antes, e a reabertura fica registrada. */
export const salvarPlano = (id: string, dados: DadosDoPlano) =>
  api<Plano>(`${base}/${id}`, { method: 'PATCH', body: dados });

export const encerrarPlano = (id: string, evidenceNote?: string | null) =>
  api<Plano>(`${base}/${id}/close`, { method: 'POST', body: { evidenceNote: evidenceNote ?? null } });

export const reabrirPlano = (id: string) =>
  api<Plano>(`${base}/${id}/reopen`, { method: 'POST' });

export type DadosDaAcao = {
  title: string; responsibleId: string;
  startDate?: string | null; dueDate?: string | null;
  reason?: string | null; area?: string | null; expectedResult?: string | null;
  kpi?: string | null; expectedGain?: number | null; costCenter?: string | null;
  rootCauseRef?: string | null; supportArea?: string | null;
  complexity?: string | null; riskLevel?: string | null;
  dependencies?: string | null; evidence?: string | null; comments?: string | null;
};

export const criarAcao = (planoId: string, dados: DadosDaAcao) =>
  api<Acao>(`${base}/${planoId}/items`, { method: 'POST', body: dados });

export const atualizarAcao = (
  acaoId: string, dados: Partial<DadosDaAcao> & { realizedGain?: number | null },
) => api<Acao>(`${base}/items/${acaoId}`, { method: 'PATCH', body: dados });

export const mudarStatusDaAcao = (
  acaoId: string, status: StatusDaAcao, reason?: string | null, progress?: number | null,
) => api<Acao>(`${base}/items/${acaoId}/status`, {
  method: 'POST', body: { status, reason: reason ?? null, progress: progress ?? null },
});

export const salvarRisco = (planoId: string, risco: {
  id?: string | null; description: string; probability?: string; impact?: string;
  mitigation?: string | null; responsibleId?: string | null; status?: string | null;
}) => api<Risco>(`${base}/${planoId}/risks`, { method: 'PUT', body: risco });

export const apagarRisco = (planoId: string, riscoId: string) =>
  api<{ deleted: boolean }>(`${base}/${planoId}/risks/${riscoId}`, { method: 'DELETE' });

export const salvarCausaRaiz = (planoId: string, dados: Partial<CausaRaizDoPlano>) =>
  api<CausaRaizDoPlano>(`${base}/${planoId}/root-cause`, { method: 'PUT', body: dados });

export const salvarLicoes = (planoId: string, dados: Partial<LicoesDoPlano>) =>
  api<LicoesDoPlano>(`${base}/${planoId}/lessons`, { method: 'PUT', body: dados });
