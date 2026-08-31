import { ConfiguracaoSla, segundosUteis } from './sla';
import { Prioridade, StatusRequisicao } from './enums';

/**
 * Farol de SLA — a leitura visual da esteira.
 *
 * O prazo é por PRIORIDADE, em horas ÚTEIS (não corridas): é o compromisso do
 * time de compras dentro do expediente. Valores padrão, ajustáveis por tenant
 * quando a configuração existir.
 */
export const PRAZO_HORAS_UTEIS: Record<Prioridade, number> = {
  EMERGENCIAL: 4,
  ALTA: 8,
  NORMAL: 24,
  BAIXA: 40,
};

export type CorFarol = 'VERDE' | 'AMARELO' | 'VERMELHO' | 'PRETO' | 'NEUTRO';

export interface LeituraFarol {
  cor: CorFarol;
  /** Consumido / prazo. Acima de 1 o SLA estourou. */
  percentual: number;
  segundosDecorridos: number;
  segundosRestantes: number;
  prazoSegundos: number;
  /** true quando o cronômetro está congelado (a bola não está com compras). */
  pausado: boolean;
  rotulo: string;
}

/** Estados terminais não têm farol: o relógio já parou de valer. */
const ESTADOS_SEM_FAROL: ReadonlySet<StatusRequisicao> = new Set([
  'RASCUNHO', 'RECEBIDA_TOTAL', 'REJEITADA', 'CANCELADA',
]);

export interface EntradaFarol {
  status: StatusRequisicao;
  prioridade: Prioridade;
  submetidaEm: string | Date | null;
  concluidaEm: string | Date | null;
  slaPausadoEm: string | Date | null;
  slaSegundosPausados: number;
}

const paraData = (v: string | Date | null): Date | null => (v ? new Date(v) : null);

function formatarDuracao(segundos: number): string {
  const absoluto = Math.abs(segundos);
  const horas = Math.floor(absoluto / 3600);
  const minutos = Math.floor((absoluto % 3600) / 60);
  if (horas >= 24) {
    const dias = Math.floor(horas / 24);
    return `${dias}d ${horas % 24}h`;
  }
  return horas > 0 ? `${horas}h ${minutos}min` : `${minutos}min`;
}

/**
 * Calcula a leitura do farol em TEMPO ÚTIL, descontando as pausas. Puro: os
 * feriados e o regime chegam na configuração, então a mesma conta vale no
 * servidor e no navegador.
 *
 *   verde    < 50% do prazo
 *   amarelo  50% a 80%
 *   vermelho 80% a 100%
 *   preto    estourou
 */
export function calcularFarol(entrada: EntradaFarol, agora: Date, config: ConfiguracaoSla): LeituraFarol {
  const submetidaEm = paraData(entrada.submetidaEm);
  const prazoSegundos = PRAZO_HORAS_UTEIS[entrada.prioridade] * 3600;

  if (!submetidaEm || ESTADOS_SEM_FAROL.has(entrada.status)) {
    return {
      cor: 'NEUTRO',
      percentual: 0,
      segundosDecorridos: 0,
      segundosRestantes: prazoSegundos,
      prazoSegundos,
      pausado: false,
      rotulo: submetidaEm ? 'Encerrada' : 'Não submetida',
    };
  }

  const fim = paraData(entrada.concluidaEm) ?? agora;
  const pausadoEm = paraData(entrada.slaPausadoEm);
  let pausas = entrada.slaSegundosPausados;
  if (pausadoEm && !entrada.concluidaEm) pausas += segundosUteis(pausadoEm, agora, config);

  const decorridos = Math.max(0, segundosUteis(submetidaEm, fim, config) - pausas);
  const percentual = prazoSegundos > 0 ? decorridos / prazoSegundos : 0;
  const restantes = prazoSegundos - decorridos;

  const cor: CorFarol =
    percentual >= 1 ? 'PRETO' : percentual >= 0.8 ? 'VERMELHO' : percentual >= 0.5 ? 'AMARELO' : 'VERDE';

  return {
    cor,
    percentual,
    segundosDecorridos: decorridos,
    segundosRestantes: restantes,
    prazoSegundos,
    pausado: pausadoEm !== null,
    rotulo: restantes >= 0 ? `${formatarDuracao(restantes)} restantes` : `${formatarDuracao(restantes)} em atraso`,
  };
}
