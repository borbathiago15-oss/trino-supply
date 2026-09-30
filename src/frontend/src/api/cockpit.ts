import { api } from './cliente';

/** Um número com o de antes ao lado — nulo em `anterior` é "não há com o que comparar". */
export interface NumeroDoCockpit { valor: number; anterior: number | null }

export interface BlocoExecutivo {
  valorComprado: NumeroDoCockpit;
  economia: NumeroDoCockpit;
  economiaPercentual: number | null;
  custosEvitados: NumeroDoCockpit;
  entregasNoPrazoPercent: number | null;
  entregasNoPrazo: number;
  entregasConcluidas: number;
  slaDeComprasPercent: number | null;
  prazoEstourado: number;
  emAndamento: number;
  backlog: number;
}

export interface EtapaDoFluxo { key: string; label: string; quantidade: number; acimaDoPrazo: number }

export interface BlocoProdutividade {
  entraramHoje: number;
  concluidosHoje: number;
  saldo: number;
  taxaDeConclusao: number | null;
  backlog: number;
  emCotacao: number;
  aguardandoAprovacao: number;
  aguardandoOc: number;
  urgentes: number;
  fluxo: EtapaDoFluxo[];
  gargalo: EtapaDoFluxo | null;
}

export type TomDoAlerta = 'alta' | 'media' | 'baixa';

export interface AlertaDoCockpit { code: string; label: string; count: number; tone: TomDoAlerta }

export interface Cockpit {
  at: string;
  executivo: BlocoExecutivo;
  produtividade: BlocoProdutividade;
  alertas: AlertaDoCockpit[];
}

export const lerCockpit = (signal?: AbortSignal) =>
  api<Cockpit>('/api/v1/cockpit', { signal });

/**
 * A variação contra o período anterior, em por cento — nula quando não há base de
 * comparação. Sem base, a tela mostra só o número: um "+100%" vindo de zero diz muito
 * menos do que parece, e na parede parece um resultado.
 */
export function variacao(n: NumeroDoCockpit): number | null {
  if (n.anterior == null || n.anterior === 0) return null;
  return Math.round(((n.valor - n.anterior) / Math.abs(n.anterior)) * 1000) / 10;
}

/**
 * Para que lado a variação é boa notícia. Não dá para deduzir do sinal: subir o valor
 * comprado é neutro, subir a economia é bom, e subir o backlog é ruim — pintar tudo de
 * verde quando sobe faria a TV comemorar o próprio atraso.
 */
export type Sentido = 'maiorMelhor' | 'menorMelhor' | 'neutro';

export const tomDaVariacao = (v: number | null, sentido: Sentido): 'ok' | 'ruim' | 'neutro' => {
  if (v == null || v === 0 || sentido === 'neutro') return 'neutro';
  return (v > 0) === (sentido === 'maiorMelhor') ? 'ok' : 'ruim';
};
