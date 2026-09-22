import type { ExcecaoDoCockpit, Gargalo, StatusDaDescarga, TipoDeAlerta } from '@/api/torre';

/**
 * A lógica do cockpit, fora do componente: cor, ordem e texto são o que decide se a
 * parede diz a verdade, e é o que precisa de teste. O JSX fica só com o desenho.
 */

/** Quanto tempo cada unidade fica na tela antes de a rotação trocar. */
export const ROTACAO_MS = 120_000;

/**
 * O ciclo que a TV percorre: a visão geral primeiro, depois cada unidade. `null` é a geral.
 *
 * <p>
 * A geral abre o ciclo de propósito: quem passa pela sala e olha por três segundos precisa
 * ver a empresa inteira, não a unidade que calhou de estar na vez. Com uma unidade só, não
 * há o que girar — alternar entre "geral" e a própria unidade mostraria o mesmo número duas
 * vezes e faria a tela parecer travada.
 * </p>
 */
export function cicloDeUnidades(unidades: string[]): (string | null)[] {
  return unidades.length <= 1 ? [null] : [null, ...unidades];
}

/** A próxima parada do ciclo. Unidade que saiu do cadastro volta para a geral. */
export function proximaUnidade(ciclo: (string | null)[], atual: string | null): string | null {
  if (ciclo.length === 0) return null;
  const i = ciclo.findIndex((u) => u === atual);
  return ciclo[(i + 1) % ciclo.length] ?? null;
}

/** Acima disto o nó da esteira acende. Os mesmos limites que o servidor usa. */
export const GARGALO_ATENCAO_H = 48;
export const GARGALO_CRITICO_H = 72;

export const CLASSE_DO_GARGALO: Record<Gargalo, string> = {
  NORMAL: 'border-slate-800 text-slate-300',
  ATENCAO: 'border-amber-500/60 text-amber-300',
  CRITICO: 'border-rose-500/70 text-rose-300',
};

export const ROTULO_DO_ALERTA: Record<TipoDeAlerta, string> = {
  ATRASO_CRITICO: 'Atraso',
  COTACAO_VENCENDO: 'Cotação',
  PROPOSTA_UNICA: 'Proposta única',
  OC_PENDENTE: 'Sem O.C.',
};

/**
 * A faixa de cor do alerta, pela gravidade. Vermelho é data estourada; âmbar é cotação
 * em risco; azul é compra aprovada esperando a O.C. — grave, mas ainda dentro do fluxo.
 */
export const CLASSE_DO_ALERTA: Record<TipoDeAlerta, string> = {
  ATRASO_CRITICO: 'border-l-rose-500 bg-rose-950/40 text-rose-200',
  COTACAO_VENCENDO: 'border-l-amber-400 bg-amber-950/30 text-amber-200',
  PROPOSTA_UNICA: 'border-l-amber-400 bg-amber-950/30 text-amber-200',
  OC_PENDENTE: 'border-l-sky-400 bg-sky-950/30 text-sky-200',
};

export const CLASSE_DA_DESCARGA: Record<StatusDaDescarga, string> = {
  NO_PRAZO: 'text-emerald-300',
  ATRASADO: 'text-rose-300',
  DESCARREGANDO: 'text-sky-300',
};

export const ROTULO_DA_DESCARGA: Record<StatusDaDescarga, string> = {
  NO_PRAZO: 'No prazo',
  ATRASADO: 'Atrasado',
  DESCARREGANDO: 'Descarregando',
};

/**
 * A ordem do radar. O servidor já entrega ordenado, mas a tela reordena porque é ela que
 * recebe a atualização de 30 em 30 segundos: o critério de aceite 3 diz que o item urgente
 * que entra assume o topo, e depender da ordem de chegada do JSON deixaria isso ao acaso.
 */
export const ordenarRadar = (linhas: ExcecaoDoCockpit[]): ExcecaoDoCockpit[] =>
  [...linhas].sort((a, b) => a.ordem - b.ordem || a.codigoReferencia.localeCompare(b.codigoReferencia));

/**
 * Quanto do caminho até a meta já foi andado, entre 0 e 1. Meta zero ou ausente devolve
 * nulo: barra cheia sem meta definida seria comemoração de nada.
 */
export const progressoDaMeta = (valor: number, meta: number): number | null =>
  meta > 0 ? Math.max(0, Math.min(1, valor / meta)) : null;

/** O SLA da semana contra a meta institucional. */
export const slaAtingido = (pct: number, meta: number) => pct >= meta;

/**
 * Listas maiores que isto rolam sozinhas: a TV não tem quem arraste a barra.
 * Abaixo disso o auto-scroll só faria o texto tremer sem motivo.
 */
export const ITENS_SEM_ROLAGEM = 5;
export const precisaRolar = (quantidade: number) => quantidade > ITENS_SEM_ROLAGEM;

/** `HH:mm:ss` do relógio do cabeçalho. */
export const horaDoRelogio = (d: Date) =>
  [d.getHours(), d.getMinutes(), d.getSeconds()]
    .map((n) => String(n).padStart(2, '0')).join(':');

/** Números grandes na parede: 12.400 vira "12,4 mil" e 1.240.000 vira "1,2 mi". */
export function compacto(valor: number): string {
  const abs = Math.abs(valor);
  if (abs >= 1_000_000) return `${(valor / 1_000_000).toFixed(1).replace('.', ',')} mi`;
  if (abs >= 1_000) return `${(valor / 1_000).toFixed(1).replace('.', ',')} mil`;
  return String(Math.round(valor));
}
