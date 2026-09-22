import { api } from './cliente';

/**
 * Plano de ação: uma tarefa com dono, prazo e 5W2H.
 *
 * O que é derivado — atraso, dias de atraso e progresso real — vem do servidor já
 * calculado. Recalcular na tela daria duas respostas para "está atrasada?", e a que
 * o usuário veria dependeria de qual tela ele abriu.
 */

export type StatusDaAcao = 'PENDENTE' | 'EM_ANDAMENTO' | 'CONCLUIDA' | 'SUSPENSA' | 'CANCELADA';

export const ROTULO_STATUS: Record<StatusDaAcao, string> = {
  PENDENTE: 'Pendente',
  EM_ANDAMENTO: 'Em andamento',
  CONCLUIDA: 'Concluída',
  SUSPENSA: 'Suspensa',
  CANCELADA: 'Cancelada',
};

/** Suspender e cancelar exigem motivo — é a regra do servidor (AC-ERR-014). */
export const EXIGE_MOTIVO: StatusDaAcao[] = ['SUSPENSA', 'CANCELADA'];
export const exigeMotivo = (s: StatusDaAcao) => EXIGE_MOTIVO.includes(s);

export interface Acao {
  id: string;
  number: string;
  title: string;
  reason: string | null;
  area: string | null;
  responsibleId: string;
  responsibleLabel: string;
  startDate: string | null;
  dueDate: string | null;
  expectedGain: number | null;
  realizedGain: number | null;
  expectedResult: string | null;
  kpi: string | null;
  costCenter: string | null;
  status: StatusDaAcao;
  statusReason: string | null;
  /** Derivados do servidor. */
  progress: number;
  late: boolean;
  daysLate: number | null;
  open: boolean;
  completedAt: string | null;
  createdByLabel: string;
  createdAt: string;
}

export interface PlacarDasAcoes {
  total: number; pendentes: number; emAndamento: number; concluidas: number;
  suspensas: number; canceladas: number; atrasadas: number;
  ganhoEsperado: number; ganhoRealizado: number;
}

export interface PaginaDeAcoes {
  itens: Acao[];
  placar: PlacarDasAcoes;
  opcoes: { responsibles: { id: string; label: string }[]; costCenters: string[] };
}

export interface FiltrosDeAcoes {
  busca?: string;
  status?: string;
  responsavel?: string;
  centroCusto?: string;
  atrasadas?: boolean;
  de?: string;
  ate?: string;
}

const base = '/api/v1/action-items';

export async function listarAcoes(f: FiltrosDeAcoes = {}, signal?: AbortSignal): Promise<PaginaDeAcoes> {
  const p = new URLSearchParams();
  if (f.busca?.trim()) p.set('q', f.busca.trim());
  if (f.status) p.set('status', f.status);
  if (f.responsavel) p.set('responsibleId', f.responsavel);
  if (f.centroCusto) p.set('costCenter', f.centroCusto);
  if (f.atrasadas) p.set('late', 'true');
  if (f.de) p.set('from', f.de);
  if (f.ate) p.set('to', f.ate);
  const s = p.toString();
  const r = await api<{ items: Acao[]; placar: PlacarDasAcoes; filterOptions?: PaginaDeAcoes['opcoes'] }>(
    `${base}/${s ? `?${s}` : ''}`, { signal });
  return {
    itens: r.items ?? [],
    placar: r.placar,
    opcoes: { responsibles: r.filterOptions?.responsibles ?? [], costCenters: r.filterOptions?.costCenters ?? [] },
  };
}

export interface DadosDaAcao {
  title: string;
  responsibleId: string;
  startDate: string | null;
  dueDate: string | null;
  reason: string | null;
  area: string | null;
  expectedResult: string | null;
  kpi: string | null;
  expectedGain: number | null;
  costCenter: string | null;
}

export const criarAcao = (d: DadosDaAcao) => api<Acao>(`${base}/`, { method: 'POST', body: d });

export const atualizarAcao = (id: string, d: Partial<DadosDaAcao> & { realizedGain?: number | null }) =>
  api<Acao>(`${base}/${id}`, { method: 'PATCH', body: d });

export const mudarStatusDaAcao = (id: string, status: StatusDaAcao, motivo?: string | null, progresso?: number) =>
  api<Acao>(`${base}/${id}/status`, { method: 'POST', body: { status, reason: motivo ?? null, progress: progresso } });
