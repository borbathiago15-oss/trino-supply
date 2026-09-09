import { api } from './cliente';

// ---- Scorecard de fornecedores (classes A/B/C/D) ---------------------------

export type Classe = 'A' | 'B' | 'C' | 'D';
export type NivelRisco = 'BAIXO' | 'MEDIO' | 'ALTO';

export const CLASSE_FORNECEDOR: Record<Classe, { classe: string; faixa: string }> = {
  A: { classe: 'bg-ok-fundo text-ok', faixa: 'score ≥ 90' },
  B: { classe: 'bg-teal-50 text-teal-800', faixa: 'score ≥ 75' },
  C: { classe: 'bg-aviso-fundo text-aviso', faixa: 'score ≥ 60' },
  D: { classe: 'bg-perigo-fundo text-perigo', faixa: 'score < 60' },
};

export const RISCO: Record<NivelRisco, { rotulo: string; classe: string }> = {
  BAIXO: { rotulo: 'Baixo', classe: 'bg-ok-fundo text-ok' },
  MEDIO: { rotulo: 'Médio', classe: 'bg-aviso-fundo text-aviso' },
  ALTO: { rotulo: 'Alto', classe: 'bg-perigo-fundo text-perigo' },
};

export interface LinhaScorecard {
  supplierId: string;
  supplierName: string;
  score: number | null;
  grade: Classe | null;
  otifPercent: number | null;
  otifMeasured: number;
  qualityPercent: number | null;
  deliveredQuantity: number;
  rejectedQuantity: number;
  winRatePercent: number | null;
  proposals: number;
  wins: number;
  orders: number;
  totalValue: number;
  riskScore: number;
  riskLevel: NivelRisco;
  riskFactors: string[];
}

/** Janelas oferecidas no seletor das telas analíticas. */
export const JANELAS = [3, 6, 12] as const;
export type Janela = (typeof JANELAS)[number];

export async function scorecardDeFornecedores(meses: Janela, signal?: AbortSignal) {
  const { items } = await api<{ items: LinhaScorecard[] }>(
    `/api/v1/analytics/supplier-scorecard?months=${meses}`, { signal });
  return items.map((s) => ({ ...s, riskFactors: s.riskFactors ?? [] }));
}

// ---- Compliance Score dos processos ---------------------------------------

export interface Penalidade { code: string; label: string; points: number; evidence: string }

export interface ProcessoAvaliado {
  quotationId: string;
  number: string;
  kind: string;
  status: string;
  costCenter: string;
  buyerLabel: string;
  score: number;
  penalties: Penalidade[];
}

export interface MediaCompliance { label: string; count: number; averageScore: number }

export interface RelatorioCompliance {
  evaluated: number;
  concluded: number;
  averageScore: number | null;
  fullCompliance: number;
  byBuyer: MediaCompliance[];
  byCostCenter: MediaCompliance[];
  items: ProcessoAvaliado[];
}

export const relatorioDeCompliance = async (signal?: AbortSignal) => {
  const r = await api<RelatorioCompliance>('/api/v1/analytics/compliance', { signal });
  return {
    ...r,
    byBuyer: r.byBuyer ?? [], byCostCenter: r.byCostCenter ?? [],
    items: (r.items ?? []).map((i) => ({ ...i, penalties: i.penalties ?? [] })),
  };
};

/** 100 é conformidade plena; abaixo de 70 o processo acumulou penalidades sérias. */
export const classeDoScore = (s: number) =>
  s === 100 ? 'bg-ok-fundo text-ok' : s >= 70 ? 'bg-aviso-fundo text-aviso' : 'bg-perigo-fundo text-perigo';

// ---- Insights & Executivo --------------------------------------------------

export interface VisaoExecutiva {
  spend: number;
  orders: number;
  processes: number;
  closedProcesses: number;
  savingTotal: number;
  referenceSavingTotal: number;
  costAvoidanceTotal: number;
  otifPercent: number | null;
  complianceAverage: number | null;
}

export type SeveridadeInsight = 'alta' | 'media' | 'info';

export interface Achado {
  code: string;
  kind: string;
  severity: SeveridadeInsight;
  title: string;
  /** O que foi encontrado. */
  evidence: string;
  /** A providência, em uma frase — descrever sem dizer o que fazer devolve o trabalho a quem lê. */
  action: string;
  /** Id da tela onde se age, no mesmo vocabulário dos avisos; null quando não há destino. */
  view: string | null;
}

export const CLASSE_ACHADO: Record<SeveridadeInsight, string> = {
  alta: 'border-perigo/30 bg-perigo-fundo',
  media: 'border-aviso/30 bg-aviso-fundo',
  info: 'border-borda bg-superficie-suave',
};

export const BADGE_ACHADO: Record<SeveridadeInsight, string> = {
  alta: 'bg-perigo-fundo text-perigo',
  media: 'bg-aviso-fundo text-aviso',
  info: 'bg-slate-100 text-slate-600',
};

export interface Backlog {
  total: number;
  unassigned: number;
  aging: { label: string; count: number }[];
  byAssignee: { label: string; count: number }[];
}

export interface RelatorioInsights {
  months: number;
  executive: VisaoExecutiva;
  backlog: Backlog;
  insights: Achado[];
}

export const relatorioDeInsights = async (meses: Janela, signal?: AbortSignal) => {
  const r = await api<RelatorioInsights>(`/api/v1/analytics/insights?months=${meses}`, { signal });
  return {
    ...r,
    insights: r.insights ?? [],
    backlog: { ...r.backlog, aging: r.backlog?.aging ?? [], byAssignee: r.backlog?.byAssignee ?? [] },
  };
};

// ---- Risco de concentração por produto -------------------------------------

export type NivelConcentracao = 'CRITICO' | 'ATENCAO';

export const CONCENTRACAO: Record<NivelConcentracao, { rotulo: string; classe: string }> = {
  CRITICO: { rotulo: 'Crítico', classe: 'bg-perigo-fundo text-perigo' },
  ATENCAO: { rotulo: 'Atenção', classe: 'bg-aviso-fundo text-aviso' },
};

export interface LinhaConcentracao {
  catalogItemId: string;
  description: string;
  total: number;
  purchases: number;
  suppliers: number;
  level: NivelConcentracao;
  /** O que fazer, já nomeando o fornecedor — número sozinho não vira providência. */
  recommendation: string;
  topSupplierId: string;
  topSupplier: string;
  topShare: number;
  topValue: number;
}

export interface RelatorioConcentracao {
  /** Piso de compras abaixo do qual o produto não entra na lista. */
  minPurchases: number;
  items: LinhaConcentracao[];
}

export const concentracaoDeFornecedor = async (signal?: AbortSignal) => {
  const r = await api<RelatorioConcentracao>('/api/v1/analytics/supplier-concentration', { signal });
  return { minPurchases: r.minPurchases, items: r.items ?? [] };
};

// ---- TCO por produto -------------------------------------------------------

export interface LinhaTco {
  catalogItemId: string;
  code: string | null;
  description: string;
  family: string | null;
  category: string | null;
  unitOfMeasure: string | null;
  quantity: number;
  orders: number;
  itemsValue: number;
  extrasValue: number;
  tcoTotal: number;
  unitPriceAvg: number;
  tcoUnitAvg: number;
  extrasPercent: number;
}

export const tcoPorProduto = async (meses: Janela, signal?: AbortSignal) =>
  (await api<{ items: LinhaTco[] }>(`/api/v1/analytics/tco?months=${meses}`, { signal })).items ?? [];
