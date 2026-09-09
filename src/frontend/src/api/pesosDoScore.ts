import { api } from './cliente';
import type { CriterioDoScore } from './cotacoes';

/**
 * A régua do score da empresa: quanto vale cada critério na comparação das propostas.
 *
 * Os `criteria` trazem rótulo e explicação junto do peso — a tela de configuração não
 * escreve texto de critério próprio, porque duas telas descrevendo o mesmo critério com
 * palavras diferentes é como um critério vira duas coisas na cabeça de quem lê.
 */
export interface PesosDoScore {
  price: number;
  delivery: number;
  payment: number;
  otif: number;
  risk: number;
  updatedAt: string;
  updatedByLabel: string;
  criteria: CriterioDoScore[];
}

export interface ReguaDoScore {
  weights: PesosDoScore;
  /** Se quem olha pode alterar. Vem do servidor: a tela não deduz papel. */
  canEdit: boolean;
}

/** Os cinco pesos, na ordem em que a tela os edita. */
export type ChaveDePeso = 'price' | 'delivery' | 'payment' | 'otif' | 'risk';
export const CHAVES_DE_PESO: ChaveDePeso[] = ['price', 'delivery', 'payment', 'otif', 'risk'];

export const lerPesosDoScore = async (signal?: AbortSignal) => {
  const r = await api<ReguaDoScore>('/api/v1/score-weights', { signal });
  return { ...r, weights: { ...r.weights, criteria: r.weights.criteria ?? [] } };
};

export const salvarPesosDoScore = (pesos: Record<ChaveDePeso, number>) =>
  api<PesosDoScore>('/api/v1/score-weights', { method: 'PUT', body: pesos });

/**
 * Soma dos pesos digitados. A tela precisa dela em tempo real: descobrir no erro do
 * servidor que a conta não fecha é o jeito lento de somar cinco números.
 */
export const somaDosPesos = (pesos: Record<ChaveDePeso, string>) =>
  CHAVES_DE_PESO.reduce((total, chave) => {
    const v = Number((pesos[chave] ?? '').replace(',', '.'));
    return total + (Number.isFinite(v) ? v : 0);
  }, 0);
