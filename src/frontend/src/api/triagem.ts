import { api } from './cliente';
import { ROTULO_PAPEL, type Papel } from '@/dominio/papeis';

/** SC de compra ou solicitação de material — as duas entram na mesma fila. */
export type TipoDemanda = 'SC' | 'MR';

export interface ItemDemanda {
  id: string;
  sequence: number;
  code: string | null;
  description: string;
  size: string | null;
  quantity: number | null;
  unitOfMeasure: string | null;
  family: string | null;
  quotationNumber: string | null;
  purchaseOrderNumber: string | null;
  processStatusLabel: string | null;
  processStatusTone: string | null;
  processStatusHint: string | null;
}

export interface Demanda {
  kind: TipoDemanda;
  id: string;
  number: string;
  costCenter: string;
  requesterLabel: string;
  summary: string | null;
  estimatedValue: number | null;
  openedAt: string | null;
  status: string;
  processStatusLabel: string | null;
  processStatusTone: string | null;
  processStatusHint: string | null;
  /** Itens em processos diferentes: a situação passa a ser por linha. */
  splitProcesses: boolean;
  priority: string;
  neededBy: string | null;
  justification: string | null;
  urgencyReason: string | null;
  urgencyImpact: string | null;
  priorityChangedByLabel: string | null;
  priorityChangeReason: string | null;
  assignedToId: string | null;
  assignedToLabel: string | null;
  assignedByLabel: string | null;
  items: ItemDemanda[];
}

export interface Responsavel { id: string; name: string; role: Papel }

export const rotuloDoResponsavel = (r: Responsavel) =>
  `${r.name} (${ROTULO_PAPEL[r.role] ?? r.role})`;

export interface RespostaTriagem {
  items: Demanda[];
  total: number;
  requesters: string[];
  assignees: string[];
  statuses: { key: string; label: string }[];
}

export type EscopoTriagem = 'TODAS' | 'NAO_ATRIBUIDAS' | 'MINHAS';

export interface FiltrosTriagem {
  escopo: EscopoTriagem;
  solicitante: string;
  responsavel: string;
  situacao: string;
}

export const FILTROS_VAZIOS: FiltrosTriagem = {
  escopo: 'TODAS', solicitante: '', responsavel: '', situacao: '',
};

const base = '/api/v1/triage';

export async function listarDemandas(f: FiltrosTriagem, signal?: AbortSignal) {
  const q = new URLSearchParams({ filter: f.escopo });
  if (f.solicitante) q.set('requester', f.solicitante);
  if (f.responsavel) q.set('assignee', f.responsavel);
  if (f.situacao) q.set('status', f.situacao);
  const r = await api<RespostaTriagem>(`${base}/?${q}`, { signal });
  return {
    ...r,
    items: (r.items ?? []).map((t) => ({ ...t, items: t.items ?? [] })),
    requesters: r.requesters ?? [], assignees: r.assignees ?? [], statuses: r.statuses ?? [],
  };
}

export const listarResponsaveis = async (signal?: AbortSignal) =>
  (await api<{ items: Responsavel[] }>(`${base}/responsibles`, { signal })).items;

export const designar = (kind: TipoDemanda, id: string, responsibleId: string | null) =>
  api<unknown>(`${base}/assign`, { method: 'POST', body: { kind, id, responsibleId } });

export const designarEmLote = (alvos: { kind: TipoDemanda; id: string }[], responsibleId: string) =>
  api<{ assigned: number; failed: { id: string; code: string; message: string }[] }>(
    `${base}/assign-batch`, { method: 'POST', body: { items: alvos, responsibleId } });

export const alterarPrioridade = (id: string, priority: 'URGENT' | 'NORMAL', reason: string, impact: string | null) =>
  api<unknown>(`${base}/priority`, { method: 'POST', body: { id, priority, reason, impact } });

// ---- aging: quanto tempo a demanda está parada na fila ---------------------

export const FAIXAS_AGING = [
  { limite: 2, rotulo: '0–2 dias', classe: 'bg-ok-fundo text-ok' },
  { limite: 5, rotulo: '3–5 dias', classe: 'bg-teal-50 text-teal-800' },
  { limite: 10, rotulo: '6–10 dias', classe: 'bg-aviso-fundo text-aviso' },
  { limite: Infinity, rotulo: '+10 dias', classe: 'bg-perigo-fundo text-perigo' },
] as const;

export const diasNaFila = (t: Pick<Demanda, 'openedAt'>, agora = Date.now()) =>
  t.openedAt ? Math.max(0, Math.floor((agora - new Date(t.openedAt).getTime()) / 86_400_000)) : 0;

/** Índice da faixa de aging: 0 é a mais nova, 3 a mais velha. */
export const faixaDeAging = (t: Pick<Demanda, 'openedAt'>, agora = Date.now()) =>
  FAIXAS_AGING.findIndex((f) => diasNaFila(t, agora) <= f.limite);
