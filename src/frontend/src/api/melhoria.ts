import { api } from './cliente';

/** As quatro fases, mais o encerramento — que é veredito, não fase do trabalho. */
export type Fase = 'PLAN' | 'DO' | 'CHECK' | 'ACT' | 'ENCERRADO';

export interface Ciclo {
  id: string;
  code: string;
  title: string;
  scope: string;
  scopeLabel: string;
  phase: Fase;
  phaseLabel: string;
  region: string | null;
  sectorId: string | null;
  areas: string | null;
  priority: string;
  ownerId: string | null;
  ownerLabel: string | null;
  indicator: string | null;
  baseline: number | null;
  goalValue: number | null;
  unit: string | null;
  goalDeadline: string | null;
  resultValue: number | null;
  goalMet: boolean | null;
  startDate: string | null;
  endDate: string | null;
  closedAt: string | null;
  closedByLabel: string | null;
  closedReason: string | null;
  createdByLabel: string;
  createdAt: string;
}

export interface PlacarDosCiclos {
  total: number; plan: number; do: number; check: number; act: number;
  encerrados: number; encerradosComPendencia: number;
}

export interface Causa {
  label: string;
  value: number | null;
  percent: number | null;
  cumulative: number | null;
  vital: boolean;
  detail: string | null;
}

export interface Analise {
  key: string;
  name: string;
  problem: string | null;
  effect: string | null;
  rootCause: string | null;
  warning: string | null;
  steps: { number: number; question: string; answer: string }[];
  groups: { key: string; label: string; items: string[] }[];
  ideas: string[];
  causes: Causa[];
}

export interface AcaoDoCiclo {
  id: string; number: string; title: string;
  responsibleId: string; responsibleLabel: string;
  dueDate: string | null; status: string; rootCauseRef: string | null;
  progress: number; late: boolean; open: boolean;
}

export interface Sinal { chave: string; severidade: 'info' | 'atencao' | 'risco'; texto: string }

export interface Leitura {
  indicador: {
    nome: string | null; baseline: number | null; meta: number | null; atual: number | null;
    unidade: string | null; sentido: string; atingiuNoNumero: boolean | null; frase: string;
  };
  prazo: {
    prazo: string | null; diasRestantes: number | null;
    percentualConsumido: number | null; vencido: boolean; frase: string;
  };
  acoes: {
    total: number; abertas: number; atrasadas: number;
    concluidas: number; suspensas: number; frase: string;
  };
  causas: { rotulo: string; vital: boolean; acoes: number; detalhe: string | null }[];
  sinais: Sinal[];
  proximoPasso: string;
  frases: string[];
}

export interface CicloCompleto {
  cycle: Ciclo;
  plan: {
    problem: string | null; currentSituation: string | null;
    toolName: string | null; toolData: string | null;
    causeAnalysis: string | null; rootCause: string | null; goalDescription: string | null;
  };
  check: { checkedOn: string | null; checkAnalysis: string | null };
  act: { standardization: string | null; lessons: string | null; newCycle: boolean };
  sectorName: string | null;
  watchers: { userId: string; label: string }[];
  costCenters: string[];
  analysis: Analise | null;
  actions: AcaoDoCiclo[];
  reading: Leitura;
}

export interface OpcoesDoModulo {
  phases: { key: string; label: string }[];
  scopes: { key: string; label: string }[];
  tools: { key: string; label: string }[];
}

const base = '/api/v1/improvement-cycles';

export const listarCiclos = (
  f: { q?: string; phase?: string; scope?: string } = {}, signal?: AbortSignal,
) => {
  const p = new URLSearchParams();
  if (f.q) p.set('q', f.q);
  if (f.phase) p.set('phase', f.phase);
  if (f.scope) p.set('scope', f.scope);
  const qs = p.toString();
  return api<{ items: Ciclo[]; placar: PlacarDosCiclos; options: OpcoesDoModulo }>(
    `${base}/${qs ? '?' + qs : ''}`, { signal });
};

export const abrirCiclo = (id: string, signal?: AbortSignal) =>
  api<CicloCompleto>(`${base}/${id}`, { signal });

export type DadosDoCiclo = Partial<{
  title: string; scope: string; region: string | null; sectorId: string | null;
  areas: string | null; priority: string; ownerId: string | null;
  startDate: string | null; endDate: string | null;
  problem: string | null; currentSituation: string | null;
  toolName: string | null; toolData: string | null;
  causeAnalysis: string | null; rootCause: string | null; goalDescription: string | null;
  indicator: string | null; baseline: number | null; goalValue: number | null;
  unit: string | null; goalDeadline: string | null;
  checkedOn: string | null; resultValue: number | null; checkAnalysis: string | null;
  standardization: string | null; lessons: string | null; newCycle: boolean;
  phase: string; costCenters: string[];
}>;

export const criarCiclo = (dados: DadosDoCiclo) =>
  api<Ciclo>(`${base}/`, { method: 'POST', body: dados });

export const salvarCiclo = (id: string, dados: DadosDoCiclo) =>
  api<Ciclo>(`${base}/${id}`, { method: 'PATCH', body: dados });

/**
 * O que a recusa por pendência devolve: o código e a lista do que sobrou. Pedir a confirmação
 * sem dizer de quê seria pedir uma assinatura em branco.
 */
export interface PendenciasDoEncerramento {
  code: string;
  message: string;
  pending: AcaoDoCiclo[];
}

export class EncerramentoPendente extends Error {
  constructor(public readonly detalhe: PendenciasDoEncerramento) { super(detalhe.message); }
}

/**
 * Encerrar é veredito, não fase. `goalMet` vai sempre explícito: o servidor recusa o
 * encerramento sem ele, e é isso que impede o silêncio de quem chamou a rota virar "não
 * atingida".
 */
export async function encerrarCiclo(
  id: string, veredito: { goalMet: boolean; reason: string; confirmPending?: boolean },
): Promise<Ciclo> {
  try {
    return await api<Ciclo>(`${base}/${id}/close`, { method: 'POST', body: veredito });
  } catch (e) {
    const corpo = (e as { corpo?: { error?: { code?: string; message?: string }; pending?: AcaoDoCiclo[] } }).corpo;
    if (corpo?.error?.code === 'PDCA-ERR-041')
      throw new EncerramentoPendente({
        code: corpo.error.code,
        message: corpo.error.message ?? 'Sobraram ações em aberto.',
        pending: corpo.pending ?? [],
      });
    throw e;
  }
}

export const reabrirCiclo = (id: string, phase?: string) =>
  api<Ciclo>(`${base}/${id}/reopen`, { method: 'POST', body: { phase: phase ?? null } });

export const mudarEscopo = (id: string, scope: string, costCenters: string[]) =>
  api<{ cycle: Ciclo; movedActions: number }>(
    `${base}/${id}/scope`, { method: 'POST', body: { scope, costCenters } });

export const definirAcompanhantes = (id: string, userIds: string[]) =>
  api<{ watchers: { userId: string; label: string }[] }>(
    `${base}/${id}/watchers`, { method: 'PUT', body: { userIds } });
