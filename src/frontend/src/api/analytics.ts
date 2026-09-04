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
