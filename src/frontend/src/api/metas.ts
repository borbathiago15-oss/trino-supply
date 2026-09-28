import { api } from './cliente';

/** Um indicador que aceita meta, com a meta mensal cadastrada (ou nenhuma). */
export interface MetaDoIndicador {
  indicator: string;
  label: string;
  unit: 'moeda' | 'pct' | 'dias' | 'qtd';
  higherIsBetter: boolean;
  /** Soma no período (a meta cresce com os meses) ou média/taxa (a meta é a mesma). */
  accumulates: boolean;
  monthlyValue: number | null;
  updatedAt: string | null;
  updatedByLabel: string | null;
}

export interface MetasDosIndicadores {
  goals: MetaDoIndicador[];
  /** Se quem olha pode alterar. Vem do servidor: a tela não deduz papel. */
  canEdit: boolean;
}

/**
 * O número do período diante da meta do período, calculado no servidor — a régua é uma só
 * para o painel e a diretoria. Indicador sem meta cadastrada não vem: o card mostra só o valor.
 */
export interface ComparacaoComMeta {
  indicador: string;
  metaDoPeriodo: number;
  valor: number;
  atingimento: number;
  faixa: 'ok' | 'atencao' | 'fora';
}

export const lerMetas = (signal?: AbortSignal) => api<MetasDosIndicadores>('/api/v1/indicator-goals', { signal });

export const salvarMetas = (goals: { indicator: string; monthlyValue: number | null }[]) =>
  api<MetasDosIndicadores>('/api/v1/indicator-goals', { method: 'PUT', body: { goals } });

export const TOM_DA_FAIXA: Record<ComparacaoComMeta['faixa'], string> = {
  ok: 'text-ok', atencao: 'text-aviso', fora: 'text-perigo',
};

/** "meta R$ 150 mil · 67% da meta": a frase curta que o card mostra, com a unidade do número. */
export const textoDaMeta = (c: ComparacaoComMeta, formatar: (v: number) => string) =>
  `meta ${formatar(c.metaDoPeriodo)} · ${c.atingimento.toLocaleString('pt-BR')}% da meta`;
