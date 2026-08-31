/**
 * Cálculo de tempo útil de SLA — DOMÍNIO PURO (sem Prisma, sem Nest, sem
 * relógio global; os feriados chegam como dados).
 *
 * Dois regimes:
 *  - COMERCIAL: 08:00–18:00, segunda a sexta, descontando os feriados do
 *    tenant (core.feriado). Um chamado aberto sexta 17h e atendido segunda 9h
 *    consumiu 2 horas úteis, não 64 corridas.
 *  - VINTE_QUATRO_SETE: relógio corrido, ininterrupto.
 *
 * FUSO: o expediente é interpretado no fuso do tenant, informado como offset
 * fixo em minutos (padrão -180 = Brasília/Recife, sem horário de verão desde
 * 2019). Offset fixo é decisão consciente: evita depender de tzdata do runtime
 * e cobre o caso brasileiro; um tenant em outro fuso configura o seu.
 */

export type RegimeSla = 'COMERCIAL' | 'VINTE_QUATRO_SETE';

export interface ConfiguracaoSla {
  regime: RegimeSla;
  /** Hora local de início do expediente (inclusive). */
  horaInicio: number;
  /** Hora local de fim do expediente (exclusive). */
  horaFim: number;
  /** Offset do fuso em minutos em relação ao UTC (Brasília = -180). */
  offsetMinutos: number;
  /** Datas de feriado no formato AAAA-MM-DD (no fuso local). */
  feriados: ReadonlySet<string>;
}

export const CONFIG_COMERCIAL_PADRAO: Omit<ConfiguracaoSla, 'feriados'> = {
  regime: 'COMERCIAL',
  horaInicio: 8,
  horaFim: 18,
  offsetMinutos: -180,
};

const MS_POR_DIA = 24 * 60 * 60 * 1000;

/** Instante UTC → "relógio local" (Date cujos getters UTC leem a hora local). */
const paraLocal = (instante: Date, offsetMinutos: number) => new Date(instante.getTime() + offsetMinutos * 60000);
const chaveDia = (local: Date) => local.toISOString().slice(0, 10);
const ehFimDeSemana = (local: Date) => local.getUTCDay() === 0 || local.getUTCDay() === 6;

/** true quando o dia local conta como útil (seg–sex e não feriado). */
export function ehDiaUtil(instante: Date, config: ConfiguracaoSla): boolean {
  const local = paraLocal(instante, config.offsetMinutos);
  return !ehFimDeSemana(local) && !config.feriados.has(chaveDia(local));
}

/**
 * Segundos ÚTEIS entre dois instantes no regime da configuração.
 * COMERCIAL soma, dia a dia, a interseção de [inicio, fim] com a janela de
 * expediente dos dias úteis; 24/7 é a diferença corrida. Intervalo invertido
 * ou vazio vale 0 — nunca negativo.
 */
export function segundosUteis(inicio: Date, fim: Date, config: ConfiguracaoSla): number {
  if (fim <= inicio) return 0;
  if (config.regime === 'VINTE_QUATRO_SETE') {
    return Math.round((fim.getTime() - inicio.getTime()) / 1000);
  }

  const inicioLocal = paraLocal(inicio, config.offsetMinutos);
  const fimLocal = paraLocal(fim, config.offsetMinutos);

  let totalMs = 0;
  // Meia-noite local do primeiro dia, andando dia a dia até cobrir o fim.
  let dia = new Date(Date.UTC(
    inicioLocal.getUTCFullYear(), inicioLocal.getUTCMonth(), inicioLocal.getUTCDate(),
  ));
  while (dia.getTime() < fimLocal.getTime()) {
    const util = !ehFimDeSemana(dia) && !config.feriados.has(chaveDia(dia));
    if (util) {
      const janelaInicio = dia.getTime() + config.horaInicio * 3600000;
      const janelaFim = dia.getTime() + config.horaFim * 3600000;
      const de = Math.max(janelaInicio, inicioLocal.getTime());
      const ate = Math.min(janelaFim, fimLocal.getTime());
      if (ate > de) totalMs += ate - de;
    }
    dia = new Date(dia.getTime() + MS_POR_DIA);
  }
  return Math.round(totalMs / 1000);
}

/**
 * Estados em que o cronômetro fica automaticamente CONGELADO: a bola não está
 * com quem o SLA mede. A pausa liga ao ENTRAR num destes estados e desliga ao
 * SAIR para um estado fora do conjunto, somando o tempo parado (em tempo útil
 * do regime) em sla_segundos_pausados.
 */
export const ESTADOS_COM_SLA_PAUSADO: ReadonlySet<string> = new Set([
  'EM_TRIAGEM',
  'DEVOLVIDA_AJUSTE',
  'EM_COTACAO',
  'APROVACAO_ALCADA',
]);

export interface EstadoPausa {
  slaPausadoEm: Date | null;
  slaSegundosPausados: number;
}

export interface AjustePausa {
  slaPausadoEm?: Date | null;
  slaSegundosPausados?: number;
}

/**
 * Decide o efeito de uma mudança de estado sobre o cronômetro. Pura: o cálculo
 * do tempo parado é injetado (`calcularSegundos`), então o mesmo código serve
 * ao regime comercial (com feriados do banco) e ao 24/7 (corrido).
 */
export function ajustarPausaSla(
  estadoAnterior: string,
  novoEstado: string,
  pausa: EstadoPausa,
  agora: Date,
  calcularSegundos: (inicio: Date, fim: Date) => number,
): AjustePausa {
  const estavaPausado = ESTADOS_COM_SLA_PAUSADO.has(estadoAnterior);
  const ficaPausado = ESTADOS_COM_SLA_PAUSADO.has(novoEstado);

  // Entrou numa zona de pausa e o cronômetro ainda corria: congela agora.
  if (ficaPausado && pausa.slaPausadoEm === null) {
    return { slaPausadoEm: agora };
  }
  // Saiu da zona de pausa com o cronômetro congelado: soma e destrava.
  if (!ficaPausado && estavaPausado && pausa.slaPausadoEm !== null) {
    return {
      slaSegundosPausados: pausa.slaSegundosPausados + Math.max(0, calcularSegundos(pausa.slaPausadoEm, agora)),
      slaPausadoEm: null,
    };
  }
  return {};
}

export interface MarcosRequisicao {
  submetidaEm: Date | null;
  triagemEm: Date | null;
  concluidaEm: Date | null;
  slaPausadoEm: Date | null;
  slaSegundosPausados: number;
}

export interface MetricaSla {
  /** Segundos úteis descontadas as pausas; null enquanto o marco final não existe. */
  segundosUteis: number | null;
  segundosCorridos: number | null;
  emAndamento: boolean;
}

/**
 * TTO — da abertura (submissão: é quando a demanda entra na fila de quem o SLA
 * mede; o rascunho é tempo do solicitante) até triagem_em.
 * TTR — da abertura até concluida_em.
 * Ambos em tempo útil do regime, descontando o acumulado de pausas e, se o
 * cronômetro estiver congelado AGORA, o trecho congelado corrente.
 */
export function calcularMetricas(
  marcos: MarcosRequisicao,
  agora: Date,
  config: ConfiguracaoSla,
): { tto: MetricaSla; ttr: MetricaSla; slaSegundosPausados: number; pausadoAgora: boolean } {
  const metrica = (ate: Date | null, descontaPausas: boolean): MetricaSla => {
    if (!marcos.submetidaEm) return { segundosUteis: null, segundosCorridos: null, emAndamento: false };
    const fim = ate ?? agora;
    const bruto = segundosUteis(marcos.submetidaEm, fim, config);
    const corrido = Math.max(0, Math.round((fim.getTime() - marcos.submetidaEm.getTime()) / 1000));
    let pausas = descontaPausas ? marcos.slaSegundosPausados : 0;
    if (descontaPausas && ate === null && marcos.slaPausadoEm) {
      pausas += segundosUteis(marcos.slaPausadoEm, agora, config);
    }
    return {
      segundosUteis: Math.max(0, bruto - pausas),
      segundosCorridos: corrido,
      emAndamento: ate === null,
    };
  };

  return {
    // TTO não desconta pausas: até a triagem ninguém pausou nada — as zonas de
    // pausa começam justamente na triagem.
    tto: metrica(marcos.triagemEm, false),
    ttr: metrica(marcos.concluidaEm, true),
    slaSegundosPausados: marcos.slaSegundosPausados,
    pausadoAgora: marcos.slaPausadoEm !== null,
  };
}
