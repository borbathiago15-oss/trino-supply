/**
 * Faixas de alçada — domínio puro, sem I/O.
 *
 * CONVENÇÃO DE FRONTEIRA (decisão explícita, o DDL não a fixa):
 * a faixa é `valor_min <= valor < valor_max`, com `valor_max = NULL` significando
 * "sem teto". Com as faixas 0–5.000, 5.000–25.000 e 25.000–∞ isso dá:
 *   R$ 4.999,99 → nível 1      R$ 5.000,00 → nível 2
 *   R$ 24.999,99 → nível 2     R$ 25.000,00 → nível 3
 * É a única leitura em que faixas adjacentes não disputam o valor da fronteira.
 */

export interface RegraAlcadaVigente {
  id: string;
  nivel: number;
  papelExigido: string;
  valorMin: number;
  valorMax: number | null;
  vigenciaInicio: Date;
  vigenciaFim: Date | null;
}

export class AlcadaError extends Error {
  readonly codigo: string;
  readonly detalhe: Record<string, unknown>;

  constructor(codigo: string, mensagem: string, detalhe: Record<string, unknown> = {}) {
    super(mensagem);
    this.name = 'AlcadaError';
    this.codigo = codigo;
    this.detalhe = detalhe;
  }
}

/** Só o dia importa nas vigências em DATE: normaliza para meia-noite UTC. */
export function soData(valor: Date): Date {
  return new Date(Date.UTC(valor.getUTCFullYear(), valor.getUTCMonth(), valor.getUTCDate()));
}

export function regraVigenteEm(regra: RegraAlcadaVigente, data: Date): boolean {
  const dia = soData(data);
  return soData(regra.vigenciaInicio) <= dia && (regra.vigenciaFim === null || dia <= soData(regra.vigenciaFim));
}

/**
 * Regra que rege o valor na data: entre as vigentes, a faixa que o contém.
 * Duas regras vigentes no mesmo nível são impossíveis (EXCLUDE no banco); se
 * nenhuma faixa cobrir o valor, é falha de configuração e não passa calado.
 */
export function regraParaValor(valor: number, regras: RegraAlcadaVigente[], data: Date): RegraAlcadaVigente {
  const vigentes = regras.filter((r) => regraVigenteEm(r, data));
  const candidatas = vigentes.filter((r) => valor >= r.valorMin && (r.valorMax === null || valor < r.valorMax));

  if (candidatas.length === 0) {
    throw new AlcadaError('ALC-ERR-001', `Nenhuma regra de alçada vigente cobre o valor ${valor}.`, { valor });
  }
  if (candidatas.length > 1) {
    throw new AlcadaError('ALC-ERR-002', 'Faixas de alçada sobrepostas para o valor — corrija o cadastro.', {
      valor,
      niveis: candidatas.map((c) => c.nivel),
    });
  }
  return candidatas[0];
}

/** Nível exigido para o valor na data. */
export function nivelExigidoPara(valor: number, regras: RegraAlcadaVigente[], data: Date): number {
  return regraParaValor(valor, regras, data).nivel;
}
